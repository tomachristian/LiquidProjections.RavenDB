using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace LiquidProjections.RavenDB
{
    internal sealed class RavenEventMapConfigurator<TProjection>
        where TProjection : class, new()
    {
        private readonly Action<TProjection, string> setIdentity;
        private readonly IEventMap<RavenProjectionContext> map;
        private IProjectionCache<TProjection> cache = new PassthroughCache<TProjection>();
        private string collectionName = typeof(TProjection).Name;
        private readonly IEnumerable<IRavenChildProjector> children;

        public RavenEventMapConfigurator(
            IEventMapBuilder<TProjection, string, RavenProjectionContext> mapBuilder, 
            Action<TProjection, string> setIdentity,
            IEnumerable<IRavenChildProjector> children = null)
        {
            this.setIdentity = setIdentity;
            if (mapBuilder == null)
            {
                throw new ArgumentNullException(nameof(mapBuilder));
            }

            map = BuildMap(mapBuilder);
            this.children = children?.ToList() ?? new List<IRavenChildProjector>();
        }

        public string CollectionName
        {
            get => collectionName;
            set
            {
                if (string.IsNullOrEmpty(value))
                {
                    throw new ArgumentException("Collection name is missing.", nameof(value));
                }

                collectionName = value;
            }
        }

        public IProjectionCache<TProjection> Cache
        {
            get => cache;
            set => cache = value ?? throw new ArgumentNullException(nameof(value));
        }

        private IEventMap<RavenProjectionContext> BuildMap(
            IEventMapBuilder<TProjection, string, RavenProjectionContext> mapBuilder)
        {
            return mapBuilder.Build(new ProjectorMap<TProjection, string, RavenProjectionContext>()
            {
                Create = OnCreate,
                Update = OnUpdate,
                Delete = OnDelete,
                Custom = (_, projector) => projector() 
            });
        }

        private async Task OnCreate(string key, RavenProjectionContext context, Func<TProjection, Task> projector, Func<TProjection, bool> shouldOverwrite)
        {
            string databaseId = BuildDatabaseId(key);
            TProjection projection = await cache.Get(databaseId, async () => await context.Session.LoadAsync<TProjection>(databaseId).ConfigureAwait(false));
            if ((projection == null) || shouldOverwrite(projection))
            {
                if (projection == null)
                {
                    projection = new TProjection();
                    setIdentity(projection, databaseId);

                    await context.Session.StoreAsync(projection);
                    cache.Add(projection);
                }
                else
                {
                    await context.Session.StoreAsync(projection).ConfigureAwait(false);
                }
                
                await projector(projection).ConfigureAwait(false);
            }
        }

        private async Task OnUpdate(string key, RavenProjectionContext context, Func<TProjection, Task> projector, Func<bool> createIfMissing)
        {
            string databaseId = BuildDatabaseId(key);
            TProjection projection = await cache.Get(databaseId, async () => await context.Session.LoadAsync<TProjection>(databaseId).ConfigureAwait(false));
            if ((projection == null) && createIfMissing())
            {
                projection = new TProjection();
                setIdentity(projection, databaseId);
                await projector(projection);

                await context.Session.StoreAsync(projection);
                cache.Add(projection);
            }
            else
            {
                if (projection != null)
                {
                    await projector(projection);
                    await context.Session.StoreAsync(projection);
                }
            }
        }

        private async Task<bool> OnDelete(string key, RavenProjectionContext context)
        {
            string databaseId = BuildDatabaseId(key);

            // If the projection is already loaded, we have to delete it via the loaded instance.
            // If the projection is not cached, we have to load it to verify that it exists.
            // Otherwise we can delete fast by id without loading the projection.
            if (context.Session.Advanced.IsLoaded(databaseId) || !await IsCached(databaseId).ConfigureAwait(false))
            {
                TProjection existingProjection = await context.Session.LoadAsync<TProjection>(databaseId);
                if (existingProjection != null)
                {
                    context.Session.Delete(existingProjection);
                    cache.Remove(key);

                    return true;
                }
                else
                {
                    return false;
                }
            }
            else
            {
                context.Session.Delete(databaseId);
                cache.Remove(databaseId);

                return true;
            }
        }

        private string BuildDatabaseId(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                throw new InvalidOperationException("Aggregate key is missing.");
            }

            return $"{collectionName}/{key}";
        }

        private async Task<bool> IsCached(string databaseId)
        {
            TProjection cachedProjection = (TProjection)await cache.TryGet(databaseId).ConfigureAwait(false);
            return cachedProjection != null;
        }

        public async Task ProjectEvent(object anEvent, RavenProjectionContext context)
        {
            foreach (IRavenChildProjector child in children)
            {
                await child.ProjectEvent(anEvent, context).ConfigureAwait(false);
            }

            await map.Handle(anEvent, context).ConfigureAwait(false);
        }
    }
}