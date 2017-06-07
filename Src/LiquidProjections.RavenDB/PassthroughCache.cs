using System;
using System.Threading.Tasks;

namespace LiquidProjections.RavenDB
{
    internal class PassthroughCache<TProjection> : IProjectionCache<TProjection>
    {
        public void Add(TProjection projection)
        {
            if (projection == null)
            {
                throw new ArgumentNullException(nameof(projection));
            }

            // Do nothing.
        }

        public Task<TProjection> Get(string key, Func<Task<TProjection>> createProjection)
        {
            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentException("Key is missing.", nameof(key));
            }

            if (createProjection == null)
            {
                throw new ArgumentNullException(nameof(createProjection));
            }

            return createProjection();
        }

        public void Remove(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentException("Key is missing.", nameof(key));
            }

            // Do nothing.
        }

        public Task<TProjection> TryGet(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentException("Key is missing.", nameof(key));
            }

            return Task.FromResult(default(TProjection));
        }
    }
}