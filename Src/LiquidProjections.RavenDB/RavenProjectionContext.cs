using Raven.Client.Documents.Session;

namespace LiquidProjections.RavenDB
{
    public class RavenProjectionContext : ProjectionContext
    {
        public IAsyncDocumentSession Session { get; set; }
    }
}