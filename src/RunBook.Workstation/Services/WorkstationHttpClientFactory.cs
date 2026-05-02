using System;
using System.Net.Http;
using System.Net;
using System.Threading;

namespace RunBook.Workstation.Services
{
    public static class WorkstationHttpClientFactory
    {
        private static readonly Lazy<HttpClient> SharedLocalServiceProbeClient = new Lazy<HttpClient>(CreateSharedLocalServiceProbeClient);

        public static HttpClient Create(HttpMessageHandler? innerHandler = null, TimeSpan? timeout = null)
        {
            var handler = innerHandler ?? new HttpClientHandler();
            if (ControlCallDetectionService.IsEnabled)
                handler = ControlCallDetectionService.CreateGuardHandler(handler);

            return new HttpClient(handler)
            {
                Timeout = timeout ?? TimeSpan.FromSeconds(20)
            };
        }

        public static HttpClient GetSharedLocalServiceProbeClient()
            => SharedLocalServiceProbeClient.Value;

        private static HttpClient CreateSharedLocalServiceProbeClient()
        {
            HttpMessageHandler handler = new SocketsHttpHandler
            {
                UseCookies = false,
                UseProxy = false,
                Proxy = null,
                AutomaticDecompression = DecompressionMethods.None,
                PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30),
                PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            };

            if (ControlCallDetectionService.IsEnabled)
                handler = ControlCallDetectionService.CreateGuardHandler(handler);

            return new HttpClient(handler, disposeHandler: false)
            {
                Timeout = System.Threading.Timeout.InfiniteTimeSpan
            };
        }
    }
}
