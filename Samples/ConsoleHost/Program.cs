using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Web.Http;
using System.Web.Http.Dispatcher;
using LiquidProjections;
using Microsoft.Owin.Hosting;
using Owin;
using Raven.Client.Documents;
using Raven.Client.Documents.Conventions;
using Raven.Client.Documents.Indexes;
using Raven.Client.Documents.Session;
using Raven.Embedded;
using TinyIoC;

namespace ConsoleHost
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            var container = TinyIoCContainer.Current;

            string executionFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            
            var eventStore = new JsonFileEventStore(Path.Combine(executionFolder, "ExampleEvents.zip"), 100);

            IDocumentStore store = BuildDocumentStore(".\\", 9001);

            container.Register<Func<IAsyncDocumentSession>>(() => store.OpenAsyncSession());
            var dispatcher = new Dispatcher(eventStore.Subscribe);

            var bootstrapper = new CountsProjector(dispatcher, store.OpenAsyncSession);

            var startOptions = new StartOptions($"http://localhost:9000");
            using (WebApp.Start(startOptions, builder => builder.UseControllers(container)))
            {
                bootstrapper.Start().Wait();

                Console.WriteLine($"HTTP API is available at http://localhost:9000/api/Statistics/CountsPerState?country=f57835c6-a818-49c0-bcb5-ab770243bb7a&kind=Permit");
                Console.WriteLine($"Management Studio available at http://localhost:9001");

                Console.ReadLine();
            }
        }

        private static IDocumentStore BuildDocumentStore(string rootDir, int? studioPort)
        {
            EmbeddedServer.Instance.StartServer(new ServerOptions
            {
                DataDirectory = rootDir,
                ServerUrl = "http://127.0.0.1:" + studioPort,
            });

            IDocumentStore documentStore = EmbeddedServer.Instance.GetDocumentStore(new DatabaseOptions("Embedded")
            {
                Conventions = new DocumentConventions()
                {
                    MaxNumberOfRequestsPerSession = 100,
                },                
            });
            
            documentStore.Initialize();

            IndexCreation.CreateIndexes(typeof(Program).Assembly, documentStore);

            return documentStore;
        }

        private static IAppBuilder UseControllers(this IAppBuilder app, TinyIoCContainer container)
        {
            HttpConfiguration configuration = BuildHttpConfiguration(container);
            app.Map("/api", a => a.UseWebApi(configuration));

            return app;
        }

        private static HttpConfiguration BuildHttpConfiguration(TinyIoCContainer container)
        {
            var configuration = new HttpConfiguration
            {
                DependencyResolver = new TinyIocWebApiDependencyResolver(container),
                IncludeErrorDetailPolicy = IncludeErrorDetailPolicy.Always
            };

            configuration.Services.Replace(typeof(IHttpControllerTypeResolver), new ControllerTypeResolver());
            configuration.MapHttpAttributeRoutes();

            return configuration;
        }

        private class ControllerTypeResolver : IHttpControllerTypeResolver
        {
            public ICollection<Type> GetControllerTypes(IAssembliesResolver assembliesResolver)
            {
                return new List<Type>
                {
                    typeof(StatisticsController)
                };
            }
        }
    }
}