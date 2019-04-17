using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LiquidProjections.Abstractions;
using Raven.Client;

namespace LiquidProjections.RavenDB
{
    /// <summary>
    /// Projects events to projections of type <typeparamref name="TProjection"/> stored in RavenDB.
    /// Keeps track of its own state stored in RavenDB in RavenCheckpoints collection.
    /// Can also have child projectors of type <see cref="IRavenChildProjector"/> which project events
    /// in the same session just before the parent projector.
    /// Throws <see cref="ProjectionException"/> when it detects errors in the event handlers.
    /// </summary>
    public class RavenProjector<TProjection>
        where TProjection : class, new()
    {
        private readonly Func<IAsyncDocumentSession> sessionFactory;
        private int batchSize;
        private readonly RavenEventMapConfigurator<TProjection> mapConfigurator;
        private string projectorName;
        private ShouldRetry shouldRetry = (exception, count) => Task.FromResult(false);

        /// <summary>
        /// Creates a new instance of <see cref="RavenProjector{TProjection}"/>.
        /// </summary>
        /// <param name="sessionFactory">The delegate that creates a new <see cref="IAsyncDocumentSession"/>.</param>
        /// <param name="mapBuilder">
        /// The <see cref="IEventMapBuilder{TProjection,TKey,TContext}"/>
        /// with already configured handlers for all the required events
        /// but not yet configured how to handle custom actions, projection creation, updating and deletion.
        /// The <see cref="IEventMap{TContext}"/> will be created from it.
        /// </param>
        /// <param name="setIdentity">
        /// Is used by the projector to set the identity of the projection.
        /// </param>
        /// <param name="children">An optional collection of <see cref="IRavenChildProjector"/> which project events
        /// in the same session just before the parent projector.</param>
        public RavenProjector(
            Func<IAsyncDocumentSession> sessionFactory,
            IEventMapBuilder<TProjection, string, RavenProjectionContext> mapBuilder,
            Action<TProjection, string> setIdentity,
            IEnumerable<IRavenChildProjector> children = null)
        {
            if (mapBuilder == null)
            {
                throw new ArgumentNullException(nameof(mapBuilder));
            }

            this.sessionFactory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory));
            mapConfigurator = new RavenEventMapConfigurator<TProjection>(mapBuilder, setIdentity, children);
        }

        /// <summary>
        /// How many transactions should be processed together in one session. Defaults to one.
        /// Should be small enough for RavenDB to be able to handle in one session.
        /// </summary>
        public int BatchSize
        {
            get { return batchSize; }
            set
            {
                if (value < 1)
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }

                batchSize = value;
            }
        }

        /// <summary>
        /// A delegate that will be executed when projecting a batch of transactions fails.
        /// This delegate returns a value that indicates if the action should be retried.
        /// </summary>
        public ShouldRetry ShouldRetry
        {
            get => shouldRetry;
            set => shouldRetry = value ?? throw new ArgumentNullException(nameof(value), "Retry policy is missing.");
        }

        /// <summary>
        /// The name of the collection in RavenDB that contains the projections.
        /// Defaults to the name of the projection type <typeparamref name="TProjection"/>.
        /// </summary>
        public string CollectionName
        {
            get => mapConfigurator.CollectionName;
            set => mapConfigurator.CollectionName = value;
        }

        /// <summary>
        /// The name of the projector that is used as the document name of the projector state in RavenCheckpoints collection.
        /// Defaults to the <see cref="CollectionName"/> if not set.
        /// </summary>
        public string ProjectorName
        {
            get => projectorName ?? CollectionName;
            set
            {
                if (string.IsNullOrEmpty(value))
                {
                    throw new ArgumentException("Projector name is missing.", nameof(value));
                }

                projectorName = value;
            }
        }

        /// <summary>
        /// A cache that can be used to avoid loading projections from the database.
        /// </summary>
        public IProjectionCache<TProjection> Cache
        {
            get => mapConfigurator.Cache;
            set => mapConfigurator.Cache = value;
        }

        /// <summary>
        /// Instructs the projector to project a collection of ordered transactions asynchronously
        /// in batches of the configured size <see cref="BatchSize"/>.
        /// </summary>
        public async Task Handle(IEnumerable<Transaction> transactions, SubscriptionInfo info)
        {
            if (transactions == null)
            {
                throw new ArgumentNullException(nameof(transactions));
            }

            foreach (IList<Transaction> batch in transactions.InBatchesOf(batchSize))
            {
                await ExecuteWithRetry(() => ProjectTransactionBatch(batch)).ConfigureAwait(false);
            }
        }

        private async Task ExecuteWithRetry(Func<Task> action)
        {
            for (int attempt = 1;;attempt++)
            {
                try
                {
                    await action();
                    break;
                }
                catch (ProjectionException exception)
                {
                    if (!await ShouldRetry(exception, attempt))
                    {
                        throw;
                    }
                }
            }
        }

        private async Task ProjectTransactionBatch(IList<Transaction> batch)
        {
            try
            {
                using (IAsyncDocumentSession session = sessionFactory())
                {
                    foreach (Transaction transaction in batch)
                    {
                        await ProjectTransaction(transaction, session).ConfigureAwait(false);
                    }

                    await StoreLastCheckpoint(session, batch.Last()).ConfigureAwait(false);
                    await session.SaveChangesAsync().ConfigureAwait(false);
                }
            }
            catch (ProjectionException projectionException)
            {
                projectionException.Projector = typeof(TProjection).ToString();
                projectionException.SetTransactionBatch(batch);
                throw;
            }
            catch (Exception exception)
            {
                var projectionException = new ProjectionException("Projector failed to project transaction batch.", exception)
                {
                    Projector = typeof(TProjection).ToString()
                };

                projectionException.SetTransactionBatch(batch);
                throw projectionException;
            }
        }

        private async Task ProjectTransaction(Transaction transaction, IAsyncDocumentSession session)
        {
            foreach (EventEnvelope eventEnvelope in transaction.Events)
            {
                var context = new RavenProjectionContext
                {
                    TransactionId = transaction.Id,
                    Session = session,
                    StreamId = transaction.StreamId,
                    TimeStampUtc = transaction.TimeStampUtc,
                    Checkpoint = transaction.Checkpoint,
                    EventHeaders = eventEnvelope.Headers,
                    TransactionHeaders = transaction.Headers
                };

                try
                {
                    await mapConfigurator.ProjectEvent(eventEnvelope.Body, context).ConfigureAwait(false);
                }
                catch (ProjectionException projectionException)
                {
                    projectionException.TransactionId = transaction.Id;
                    projectionException.CurrentEvent = eventEnvelope;
                    throw;
                }
                catch (Exception exception)
                {
                    throw new ProjectionException("Projector failed to project an event.", exception)
                    {
                        TransactionId = transaction.Id,
                        CurrentEvent = eventEnvelope
                    };
                }
            }
        }

        private async Task StoreLastCheckpoint(IAsyncDocumentSession session, Transaction transaction)
        {
            try
            {
                await session.StoreAsync(new ProjectorState
                {
                    Id = GetCheckpointId(),
                    Checkpoint = transaction.Checkpoint,
                    LastUpdateUtc = DateTime.UtcNow
                }).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                throw new ProjectionException("Projector failed to store last checkpoint.", exception);
            }
        }

        /// <summary>
        /// Asynchronously determines the checkpoint of the last projected transaction.
        /// </summary>
        public async Task<long?> GetLastCheckpoint()
        {
            using (IAsyncDocumentSession session = sessionFactory())
            {
                var state = await session.LoadAsync<ProjectorState>(GetCheckpointId()).ConfigureAwait(false);
                return state?.Checkpoint;
            }
        }

        private string GetCheckpointId() => "RavenCheckpoints/" + ProjectorName;
    }
}