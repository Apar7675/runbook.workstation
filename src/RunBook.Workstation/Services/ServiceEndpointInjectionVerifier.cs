#if RUNBOOK_INTERNAL_DIAGNOSTICS
using RunBook.Workstation.Models;
using RunBook.Workstation.ViewModels;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RunBook.Workstation.Services
{
    internal static class ServiceEndpointInjectionVerifier
    {
        private const string ResultFileName = "service-endpoint-injection-verification.json";
        private static readonly JsonSerializerOptions ResultJsonOptions = new JsonSerializerOptions { WriteIndented = true };
        private static readonly ConcurrentBag<int> ObservedNetworkPorts = new ConcurrentBag<int>();

        internal static async Task<VerificationResult> RunAsync()
        {
            while (ObservedNetworkPorts.TryTake(out _))
            {
            }

            var result = new VerificationResult { StartedAtUtc = DateTime.UtcNow };

            await RunTestAsync(result, "production_default_is_exact_and_intercepted", VerifyProductionDefaultAsync).ConfigureAwait(false);
            await RunTestAsync(result, "unique_loopback_port_is_honored", VerifyInjectedHealthRouteAsync).ConfigureAwait(false);
            await RunTestAsync(result, "representative_non_health_route_is_injected", VerifyInjectedRegistrationRouteAsync).ConfigureAwait(false);
            await RunTestAsync(result, "two_clients_have_independent_endpoints", VerifyIndependentClientsAsync).ConfigureAwait(false);
            await RunTestAsync(result, "settings_cannot_mutate_injected_endpoint", VerifySettingsCannotRedirectClientAsync).ConfigureAwait(false);
            await RunTestAsync(result, "non_loopback_host_is_rejected", () => VerifyRejectedAsync("http://example.test:41001")).ConfigureAwait(false);
            await RunTestAsync(result, "https_is_rejected", () => VerifyRejectedAsync("https://localhost:41001")).ConfigureAwait(false);
            await RunTestAsync(result, "missing_port_is_rejected", () => VerifyRejectedAsync("http://localhost")).ConfigureAwait(false);
            await RunTestAsync(result, "credentials_are_rejected", () => VerifyRejectedAsync("http://user:password@localhost:41001")).ConfigureAwait(false);
            await RunTestAsync(result, "query_is_rejected", () => VerifyRejectedAsync("http://localhost:41001/?test=1")).ConfigureAwait(false);
            await RunTestAsync(result, "empty_query_marker_is_rejected", () => VerifyRejectedAsync("http://localhost:41001/?")).ConfigureAwait(false);
            await RunTestAsync(result, "fragment_is_rejected", () => VerifyRejectedAsync("http://localhost:41001/#test")).ConfigureAwait(false);
            await RunTestAsync(result, "empty_fragment_marker_is_rejected", () => VerifyRejectedAsync("http://localhost:41001/#")).ConfigureAwait(false);
            await RunTestAsync(result, "path_is_rejected", () => VerifyRejectedAsync("http://localhost:41001/service")).ConfigureAwait(false);
            await RunTestAsync(result, "traversal_is_rejected", () => VerifyRejectedAsync("http://localhost:41001/../service")).ConfigureAwait(false);
            await RunTestAsync(result, "dot_path_normalized_to_root_is_rejected", () => VerifyRejectedAsync("http://localhost:41001/.")).ConfigureAwait(false);
            await RunTestAsync(result, "parent_path_normalized_to_root_is_rejected", () => VerifyRejectedAsync("http://localhost:41001/service/..")).ConfigureAwait(false);
            await RunTestAsync(result, "nested_traversal_normalized_to_root_is_rejected", () => VerifyRejectedAsync("http://localhost:41001/a/../")).ConfigureAwait(false);
            await RunTestAsync(result, "encoded_dot_path_is_rejected", () => VerifyRejectedAsync("http://localhost:41001/%2e")).ConfigureAwait(false);
            await RunTestAsync(result, "encoded_parent_path_is_rejected", () => VerifyRejectedAsync("http://localhost:41001/a/%2e%2e/")).ConfigureAwait(false);
            await RunTestAsync(result, "relative_uri_is_rejected", VerifyRelativeUriRejectedAsync).ConfigureAwait(false);
            await RunTestAsync(result, "noncanonical_loopback_spelling_is_rejected", () => VerifyRejectedAsync("http://127.000.000.001:41001")).ConfigureAwait(false);
            await RunTestAsync(result, "invalid_injection_never_sends_or_falls_back", VerifyInvalidInjectionFailsBeforeSendAsync).ConfigureAwait(false);
            await RunTestAsync(result, "main_view_model_uses_injected_client", VerifyMainViewModelInjectionAsync).ConfigureAwait(false);
            await RunTestAsync(result, "normal_main_view_model_constructor_remains_public", VerifyMainViewModelConstructorShapeAsync).ConfigureAwait(false);

            result.CompletedAtUtc = DateTime.UtcNow;
            result.NetworkPorts = ObservedNetworkPorts.Distinct().OrderBy(port => port).ToArray();
            result.Passed = result.Tests.All(test => test.Passed) && result.NetworkPorts.All(port => port != 30112);
            SaveResult(result);
            return result;
        }

        private static async Task RunTestAsync(VerificationResult result, string name, Func<Task> test)
        {
            try
            {
                await test().ConfigureAwait(false);
                result.Tests.Add(new VerificationTest { Name = name, Passed = true });
            }
            catch (Exception ex)
            {
                result.Tests.Add(new VerificationTest
                {
                    Name = name,
                    Passed = false,
                    Detail = ex.GetType().Name + ": " + ex.Message
                });
            }
        }

        private static async Task VerifyProductionDefaultAsync()
        {
            var handler = new RecordingHandler(HealthyResponse);
            using var httpClient = new HttpClient(handler);
            var client = new RunBookWorkstationApiClient(httpClient, () => null);
            await client.GetLocalHealthAsync(new WorkstationSettings { DesktopBaseUrl = "http://127.0.0.1:49999" }, CancellationToken.None).ConfigureAwait(false);

            Assert(handler.Requests.Count == 1, "The production-default test did not capture exactly one request.");
            Assert(
                string.Equals(handler.Requests.Single(), "http://localhost:30112/api/workstation-local/health", StringComparison.Ordinal),
                "The production constructor did not use the exact localhost:30112 endpoint.");
        }

        private static async Task VerifyInjectedHealthRouteAsync()
        {
            await using var server = new LoopbackHttpServer(HealthyResponse);
            using var client = WorkstationHttpClientFactory.Create(timeout: TimeSpan.FromSeconds(5));
            var api = new RunBookWorkstationApiClient(server.BaseUri, client, () => null);
            await api.GetLocalHealthAsync(new WorkstationSettings { DesktopBaseUrl = "http://localhost:30112" }, CancellationToken.None).ConfigureAwait(false);

            Assert(server.Requests.Any(request => request == "GET /api/workstation-local/health"), "The injected health route was not observed.");
            Assert(server.Port != 30112, "The OS assigned the forbidden production port to the test listener.");
        }

        private static async Task VerifyInjectedRegistrationRouteAsync()
        {
            await using var server = new LoopbackHttpServer(HealthyResponse);
            using var client = WorkstationHttpClientFactory.Create(timeout: TimeSpan.FromSeconds(5));
            var api = new RunBookWorkstationApiClient(server.BaseUri, client, () => null);
            var settings = SyntheticSettings("http://localhost:30112");
            var registration = await api.RegisterAsync(settings, CancellationToken.None).ConfigureAwait(false);

            Assert(server.Requests.Any(request => request == "POST /api/workstation-local/register"), "The injected registration route was not observed.");
            Assert(string.Equals(registration.ShopId, settings.ShopId, StringComparison.Ordinal), "The synthetic registration response was not parsed.");
        }

        private static async Task VerifyIndependentClientsAsync()
        {
            await using var firstServer = new LoopbackHttpServer(HealthyResponse);
            await using var secondServer = new LoopbackHttpServer(HealthyResponse);
            using var firstHttpClient = WorkstationHttpClientFactory.Create(timeout: TimeSpan.FromSeconds(5));
            using var secondHttpClient = WorkstationHttpClientFactory.Create(timeout: TimeSpan.FromSeconds(5));
            var first = new RunBookWorkstationApiClient(firstServer.BaseUri, firstHttpClient, () => null);
            var second = new RunBookWorkstationApiClient(secondServer.BaseUri, secondHttpClient, () => null);

            await Task.WhenAll(
                first.GetLocalHealthAsync(SyntheticSettings("http://localhost:30112"), CancellationToken.None),
                second.GetLocalHealthAsync(SyntheticSettings("http://localhost:30112"), CancellationToken.None)).ConfigureAwait(false);

            Assert(firstServer.Port != secondServer.Port, "The independent clients did not receive different loopback ports.");
            Assert(firstServer.Requests.Count == 1 && secondServer.Requests.Count == 1, "A client request crossed instance boundaries.");
        }

        private static async Task VerifySettingsCannotRedirectClientAsync()
        {
            await using var server = new LoopbackHttpServer(HealthyResponse);
            using var client = WorkstationHttpClientFactory.Create(timeout: TimeSpan.FromSeconds(5));
            var api = new RunBookWorkstationApiClient(server.BaseUri, client, () => null);
            var settings = SyntheticSettings("http://localhost:30112");
            settings.DesktopBaseUrl = "http://example.test:59999";

            await api.GetLocalHealthAsync(settings, CancellationToken.None).ConfigureAwait(false);
            Assert(server.Requests.Count == 1, "A mutable setting redirected the injected client.");
        }

        private static Task VerifyRejectedAsync(string value)
        {
            AssertThrows<ArgumentException>(() => new RunBookWorkstationApiClient(new Uri(value, UriKind.Absolute)));
            return Task.CompletedTask;
        }

        private static Task VerifyRelativeUriRejectedAsync()
        {
            AssertThrows<ArgumentException>(() => new RunBookWorkstationApiClient(new Uri("service-endpoint", UriKind.Relative)));
            return Task.CompletedTask;
        }

        private static Task VerifyInvalidInjectionFailsBeforeSendAsync()
        {
            var handler = new RecordingHandler(HealthyResponse);
            using var httpClient = new HttpClient(handler);
            AssertThrows<ArgumentException>(() => new RunBookWorkstationApiClient(new Uri("http://remote.test:41001"), httpClient, () => null));
            Assert(handler.Requests.Count == 0, "Invalid injection sent a request before failing.");
            return Task.CompletedTask;
        }

        private static Task VerifyMainViewModelInjectionAsync()
        {
            return VerifyMainViewModelInjectionCoreAsync();
        }

        private static async Task VerifyMainViewModelInjectionCoreAsync()
        {
            await using var server = new LoopbackHttpServer(BlockedHealthResponse);
            var viewModel = new MainViewModel(server.BaseUri);

            Assert(server.Requests.Any(request => request == "GET /api/workstation-local/health"), "MainViewModel did not use its injected API client during boot.");
            Assert(viewModel.StatusText.IndexOf("active company data folder", StringComparison.OrdinalIgnoreCase) >= 0, "MainViewModel did not consume the injected health response.");
            Assert(server.Port != 30112, "MainViewModel used the forbidden production port during its injection test.");
        }

        private static Task VerifyMainViewModelConstructorShapeAsync()
        {
            var publicDefault = typeof(MainViewModel).GetConstructor(Type.EmptyTypes);
            var publicUri = typeof(MainViewModel).GetConstructor(new[] { typeof(Uri) });
            Assert(publicDefault != null, "The normal public MainViewModel constructor was removed.");
            Assert(publicUri == null, "The test-only MainViewModel URI seam became public.");
            return Task.CompletedTask;
        }

        private static WorkstationSettings SyntheticSettings(string desktopBaseUrl)
        {
            return new WorkstationSettings
            {
                DesktopBaseUrl = desktopBaseUrl,
                ShopId = "shop-endpoint-test",
                ShopName = "Endpoint Test Shop",
                WorkstationId = "workstation-endpoint-test",
                WorkstationName = "ENDPOINT-TEST",
                PairingCode = "TEST123"
            };
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }

        private static void AssertThrows<TException>(Action action) where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException)
            {
                return;
            }

            throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
        }

        private static void SaveResult(VerificationResult result)
        {
            Directory.CreateDirectory(WorkstationStorageService.MachineFolder);
            var path = Path.Combine(WorkstationStorageService.MachineFolder, ResultFileName);
            File.WriteAllText(path, JsonSerializer.Serialize(result, ResultJsonOptions), Encoding.UTF8);
        }

        private const string HealthyResponse = "{\"ok\":true,\"status\":\"healthy\",\"shop_id\":\"shop-endpoint-test\",\"company_name\":\"Endpoint Test Shop\",\"safe_company_name\":\"ENDPOINTTEST\",\"company_data_folder_name\":\"ENDPOINTTEST_DATA\",\"active_company_data_root\":\"C:\\\\Synthetic\\\\ENDPOINTTEST_DATA\",\"listening_port\":1}";
        private const string BlockedHealthResponse = "{\"ok\":false,\"status\":\"company_data_missing\",\"error_message\":\"Active company data folder is missing.\"}";

        private sealed class RecordingHandler : HttpMessageHandler
        {
            private readonly string _responseJson;

            internal RecordingHandler(string responseJson)
            {
                _responseJson = responseJson;
            }

            internal ConcurrentQueue<string> Requests { get; } = new ConcurrentQueue<string>();

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Requests.Enqueue(request.RequestUri?.AbsoluteUri ?? "");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(_responseJson, Encoding.UTF8, "application/json")
                });
            }
        }

        private sealed class LoopbackHttpServer : IAsyncDisposable
        {
            private readonly TcpListener _listener;
            private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
            private readonly Task _acceptLoop;
            private readonly string _healthResponse;

            internal LoopbackHttpServer(string healthResponse)
            {
                _healthResponse = healthResponse;
                _listener = new TcpListener(IPAddress.Loopback, 0);
                _listener.Start();
                Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
                ObservedNetworkPorts.Add(Port);
                BaseUri = new Uri($"http://127.0.0.1:{Port}", UriKind.Absolute);
                _acceptLoop = AcceptLoopAsync();
            }

            internal int Port { get; }
            internal Uri BaseUri { get; }
            internal ConcurrentQueue<string> Requests { get; } = new ConcurrentQueue<string>();

            private async Task AcceptLoopAsync()
            {
                while (!_cancellation.IsCancellationRequested)
                {
                    TcpClient client;
                    try
                    {
                        client = await _listener.AcceptTcpClientAsync(_cancellation.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (ObjectDisposedException)
                    {
                        break;
                    }

                    await HandleClientAsync(client, _cancellation.Token).ConfigureAwait(false);
                }
            }

            private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
            {
                using (client)
                using (var stream = client.GetStream())
                using (var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true))
                {
                    var requestLine = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) ?? "";
                    var requestParts = requestLine.Split(' ');
                    var method = requestParts.Length > 0 ? requestParts[0] : "";
                    var path = requestParts.Length > 1 ? requestParts[1] : "";
                    var contentLength = 0;

                    while (true)
                    {
                        var header = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                        if (string.IsNullOrEmpty(header))
                            break;
                        if (header.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                            int.TryParse(header.Substring("Content-Length:".Length).Trim(), out contentLength);
                    }

                    if (contentLength > 0)
                    {
                        var remaining = contentLength;
                        var buffer = new char[Math.Min(contentLength, 4096)];
                        while (remaining > 0)
                        {
                            var read = await reader.ReadAsync(buffer.AsMemory(0, Math.Min(remaining, buffer.Length)), cancellationToken).ConfigureAwait(false);
                            if (read == 0)
                                break;
                            remaining -= read;
                        }
                    }

                    Requests.Enqueue($"{method} {path}");
                    var responseJson = path.StartsWith("/api/workstation-local/register", StringComparison.Ordinal)
                        ? "{\"ok\":true,\"existing\":false,\"workstation\":{\"id\":\"workstation-endpoint-test\",\"shop_id\":\"shop-endpoint-test\",\"name\":\"ENDPOINT-TEST\",\"status\":\"active\",\"device_token\":\"synthetic-token\",\"is_active\":true,\"enrollment_version\":1}}"
                        : _healthResponse;
                    var payload = Encoding.UTF8.GetBytes(responseJson);
                    var responseHeaders = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(responseHeaders, cancellationToken).ConfigureAwait(false);
                    await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
                    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
            }

            public async ValueTask DisposeAsync()
            {
                _cancellation.Cancel();
                _listener.Stop();
                try
                {
                    await _acceptLoop.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
                finally
                {
                    _cancellation.Dispose();
                }
            }
        }

        internal sealed class VerificationResult
        {
            public bool Passed { get; set; }
            public DateTime StartedAtUtc { get; set; }
            public DateTime CompletedAtUtc { get; set; }
            public List<VerificationTest> Tests { get; } = new List<VerificationTest>();
            public int[] NetworkPorts { get; set; } = Array.Empty<int>();
        }

        internal sealed class VerificationTest
        {
            public string Name { get; set; } = "";
            public bool Passed { get; set; }
            public string Detail { get; set; } = "";
        }
    }
}
#endif
