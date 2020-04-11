using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;
using System;
using System.Net.Http;

namespace AspNet.Cache.Test.WebServer
{
    public class StartTestServer : IDisposable
    {
        private readonly IHost _host;

        protected readonly HttpClient client;

        public StartTestServer()
        {
            var _hostBuilder = new HostBuilder()
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

        #region IDisposable Support

        private bool disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    client.Dispose();
                    _host.Dispose();
                }
                disposedValue = true;
            }
        }

        ~StartTestServer()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion IDisposable Support
    }
}
