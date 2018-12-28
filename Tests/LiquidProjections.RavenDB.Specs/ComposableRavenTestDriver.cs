using Raven.Client.Documents;
using Raven.TestDriver;

namespace LiquidProjections.RavenDB.Specs
{
    internal class ComposableRavenTestDriver : RavenTestDriver
    {
        public IDocumentStore GetDocumentStore()
        {
            return base.GetDocumentStore();
        }
    }
}