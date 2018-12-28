using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.Serialization.Formatters;
using System.Threading.Tasks;
using LiquidProjections;
using LiquidProjections.Abstractions;
using Newtonsoft.Json;

namespace ConsoleHost
{
    public class JsonFileEventStore : IDisposable
    {
        private const int AverageEventsPerTransaction = 6;
        private readonly int pageSize;
        private ZipArchive zip;
        private readonly Queue<ZipArchiveEntry> entryQueue;
        private StreamReader currentReader = null;
        private static long lastCheckpoint = 0;

        public JsonFileEventStore(string filePath, int pageSize)
        {
            this.pageSize = pageSize;
            zip = ZipFile.Open(filePath, ZipArchiveMode.Read);
            entryQueue = new Queue<ZipArchiveEntry>(zip.Entries.Where(e => e.Name.EndsWith(".json")));
        }

        public IDisposable Subscribe(long? lastProcessedCheckpoint, Subscriber subscriber, string subscriptionId)
        {
            var subscription = new Subscription(lastProcessedCheckpoint ?? 0, subscriber);
            
            Task.Run(async () =>
            {
                Task<Transaction[]> loader = LoadNextPageAsync();
                Transaction[] transactions = await loader;

                while (transactions.Length > 0)
                {
                    // Start loading the next page on a separate thread while we have the subscriber handle the previous transactions.
                    loader = LoadNextPageAsync();

                    await subscription.Send(transactions);

                    transactions = await loader;
                }
            });

            return subscription;
        }

        private Task<Transaction[]> LoadNextPageAsync()
        {
            return Task.Run(() =>
            {
                var transactions = new List<Transaction>();

                var transaction = new Transaction
                {
                    Checkpoint = ++lastCheckpoint
                };

                string json;

                do
                {
                    json = CurrentReader.ReadLine();

                    if (json != null)
                    {
                        transaction.Events.Add(new EventEnvelope
                        {
                            Body = JsonConvert.DeserializeObject(json, new JsonSerializerSettings
                            {
                                TypeNameHandling = TypeNameHandling.All,
                                TypeNameAssemblyFormatHandling = TypeNameAssemblyFormatHandling.Full,
                            })
                        });
                    }

                    if ((transaction.Events.Count == AverageEventsPerTransaction) || (json == null))
                    {
                        if (transaction.Events.Count > 0)
                        {
                            transactions.Add(transaction);
                        }

                        transaction = new Transaction
                        {
                            Checkpoint = ++lastCheckpoint
                        };
                    }
                }
                while ((json != null) && (transactions.Count < pageSize));

                return transactions.ToArray();
            });
        }

        private StreamReader CurrentReader => 
            currentReader ?? (currentReader = new StreamReader(entryQueue.Dequeue().Open()));

        public void Dispose()
        {
            zip.Dispose();
            zip = null;
        }

        internal class Subscription : IDisposable
        {
            private readonly long lastProcessedCheckpoint;
            private readonly Subscriber subscriber;
            private bool disposed;

            public Subscription(long lastProcessedCheckpoint, Subscriber subscriber)
            {
                this.lastProcessedCheckpoint = lastProcessedCheckpoint;
                this.subscriber = subscriber;
            }

            public async Task Send(IEnumerable<Transaction> transactions)
            {
                if (!disposed)
                {
                    Transaction[] readOnlyList = transactions.Where(t => t.Checkpoint > lastProcessedCheckpoint).ToArray();
                    if (readOnlyList.Length > 0)
                    {
                        await subscriber.HandleTransactions(readOnlyList, new SubscriptionInfo
                        {
                            Id = "subscription",
                            Subscription = this
                        });
                    }
                }
                else
                {
                    throw new ObjectDisposedException("");
                }
            }

            public void Dispose()
            {
                disposed = true;
            }
        }
    }
}