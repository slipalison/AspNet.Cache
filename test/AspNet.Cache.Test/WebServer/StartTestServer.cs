using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;
using System.Net.Http;

namespace AspNet.Cache.Test.WebServer
{
    public class StartTestServer
    {
        private readonly IHostBuilder _hostBuilder;
        private readonly IHost _host;
        protected readonly HttpClient client;

        public StartTestServer()
        {
            _hostBuilder = new HostBuilder()
                .UseEnvironment("Test")
                .ConfigureWebHost(webHost =>
                {
                    webHost.UseTestServer();

                    webHost.UseStartup<StartupTest>();
                });

            _host = _hostBuilder.Start();

            client = _host.GetTestClient();
            client.DefaultRequestHeaders.Add("X-Correlation-Id", "Teste");
        }
    }
}
