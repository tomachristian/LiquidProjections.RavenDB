using System;
using System.Threading.Tasks;
using FluidCaching;

namespace LiquidProjections.RavenDB
{
    public class LruProjectionCache<TProjection> : IProjectionCache<TProjection> where TProjection : class
    {
        private readonly Func<TProjection, string> getKey;
        private readonly IIndex<string, TProjection> index;
        private readonly FluidCache<TProjection> cache;

        public LruProjectionCache(int capacity, TimeSpan minimumRetention, TimeSpan maximumRetention, Func<TProjection, string> getKey, Func<DateTime> getUtcNow)
        {
            this.getKey = getKey;
            if (capacity < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            if (minimumRetention < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(minimumRetention));
            }

            if (maximumRetention < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumRetention));
            }

            if (minimumRetention > maximumRetention)
            {
                throw new ArgumentException("Minimum retention is greater than maximum retention.");
            }

            if (getUtcNow == null)
            {
                throw new ArgumentNullException(nameof(getUtcNow));
            }

            cache = new FluidCache<TProjection>(capacity, minimumRetention, maximumRetention, () => getUtcNow());
            index = cache.AddIndex("projections", projection => getKey(projection));
        }

        public long Hits => cache.Statistics.Hits;

        public long Misses => cache.Statistics.Misses;

        public long CurrentCount => cache.Statistics.Current;

        public async Task<TProjection> Get(string key, Func<Task<TProjection>> createProjection)            
        {
            return (TProjection)await index.GetItem(key, async _ => await createProjection());
        }

        public void Remove(string key)
        {
            index.Remove(key);
        }

        public void Add(TProjection projection)
        {
            cache.Add(projection);
        }

        public Task<TProjection> TryGet(string key)
        {
            return index.GetItem(key);
        }
    }
}