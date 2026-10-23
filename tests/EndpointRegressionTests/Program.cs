using RunBook.Workstation.Models;
using RunBook.Workstation.Services;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

var passed = 0;
void Assert(bool result, string label) { if (!result) throw new InvalidOperationException(label); passed++; }
void Reject(string value, bool lan = false)
{
    try { WorkstationServiceEndpoint.Parse(value, lan); }
    catch (ArgumentException) { passed++; return; }
    throw new InvalidOperationException("Unsafe endpoint accepted: " + value);
}
Assert(WorkstationServiceEndpoint.Resolve(new()).AbsoluteUri == "http://localhost:30112/", "default remains loopback");
Assert(WorkstationServiceEndpoint.Parse("http://192.168.2.40:30112", true).Host == "192.168.2.40", "explicit private LAN accepted");
Assert(WorkstationServiceEndpoint.Parse("https://10.0.0.5:30112", true).Scheme == "https", "TLS allowed without disabling certificate validation");
var defaultPort = WorkstationServiceEndpoint.Parse("http://127.0.0.1:80", false);
Assert(WorkstationServiceEndpoint.Parse(WorkstationServiceEndpoint.Canonical(defaultPort), false) == defaultPort, "pairing preserves an explicit default port");
foreach (var value in new[] {
    "http://192.168.2.40:30112", "http://example.com:30112", "http://127.000.000.001:30112",
    "http://user:password@localhost:30112", "http://localhost", "file:///tmp/data",
    "http://localhost:30112/path", "http://localhost:30112/..", "http://localhost:30112/./",
    "http://localhost:30112/?", "http://localhost:30112/#", " http://localhost:30112",
    "http://localhost:30112/\\evil" }) Reject(value);
foreach (var value in new[] { "http://8.8.8.8:30112", "http://169.254.169.254:30112", "http://0.0.0.0:30112", "http://shop.example:30112", "http://224.0.0.1:30112" }) Reject(value, true);

await using var primary = new SyntheticService();
await using var other = new SyntheticService();
var settings = new WorkstationSettings { DesktopBaseUrl = primary.BaseUrl, ShopId = "shop-1", WorkstationId = "kiosk-1", WorkstationName = "kiosk" };
WorkstationRegistrationSnapshot? registration = null;
using var transport = WorkstationHttpClientFactory.Create(timeout: TimeSpan.FromSeconds(5));
var api = new RunBookWorkstationApiClient(transport, () => registration);
await api.GetLocalHealthAsync(settings, CancellationToken.None);
registration = await api.RegisterAsync(settings, CancellationToken.None);
Assert(registration.ServiceEndpoint == primary.BaseUrl + "/", "pairing records selected endpoint");
await api.GetLocalAuthPackageAsync(settings, CancellationToken.None);
await api.LoginLocalEmployeeAsync(settings, "", "11", "E11", "1234", CancellationToken.None);
var session = new WorkstationSessionSnapshot { Token = "employee-secret", ShopId = "shop-1", WorkstationId = "kiosk-1" };
await api.GetSessionMeAsync(settings, session, CancellationToken.None);
await api.GetWorkOrdersAsync(settings, session, CancellationToken.None);
await api.GetWorkOrderDrawingsAsync(settings, session, 12, CancellationToken.None);
await api.DownloadDrawingAsync(settings, session, "/api/workstation-local/work-orders/12/drawings/main/content", CancellationToken.None);
Assert(primary.Requests.Count == 8, "health, enrollment, auth, login, session, jobs and drawings share configured endpoint");
Assert(primary.Requests.Where(r => r.Path != "/api/workstation-local/health" && r.Path != "/api/workstation-local/register")
    .All(r => r.Headers.TryGetValue("Authorization", out var value) && value == "Bearer device-secret"), "device bearer preserved");
Assert(primary.Requests.Where(r => r.Path.Contains("work-orders") || r.Path.EndsWith("session/me"))
    .All(r => r.Headers.TryGetValue("X-RunBook-Employee-Session", out var value) && value == "employee-secret"), "employee session preserved");

var probes = new WorkstationConnectionStatusService();
var probe = await probes.ProbeLocalHostAsync(settings);
Assert(probe.Succeeded, "health probe uses selected endpoint");
Assert(primary.Requests.Count == 9, "probe did not visit hardcoded production port");

settings.DesktopBaseUrl = other.BaseUrl;
var pinRejected = false;
try { await api.GetWorkOrdersAsync(settings, session, CancellationToken.None); }
catch (InvalidOperationException ex) when (ex.Message.StartsWith("PAIRING_REQUIRED", StringComparison.Ordinal)) { pinRejected = true; }
Assert(pinRejected && other.Requests.Count == 0, "endpoint change cannot leak saved device or employee credentials");
settings.DesktopBaseUrl = primary.BaseUrl;
primary.RedirectJobsTo = other.BaseUrl + "/api/workstation-local/work-orders";
var redirectRejected = false;
try { await api.GetWorkOrdersAsync(settings, session, CancellationToken.None); }
catch (Exception) { redirectRejected = true; }
Assert(redirectRejected && other.Requests.Count == 0, "protected request never follows redirect to a different endpoint");

var legacy = new WorkstationRegistrationSnapshot { DeviceToken = "legacy" };
Assert(WorkstationServiceEndpoint.RegistrationMatches(legacy, new Uri(WorkstationServiceEndpoint.DefaultUrl)), "legacy pairing remains default-only");
Assert(!WorkstationServiceEndpoint.RegistrationMatches(legacy, new Uri(primary.BaseUrl)), "legacy credentials do not migrate to a different port");
var moving = new WorkstationSettings { DesktopBaseUrl = primary.BaseUrl };
using (var changingTransport = new HttpClient(new ChangingRegistrationHandler(() => moving.DesktopBaseUrl = other.BaseUrl)))
{
    var changingApi = new RunBookWorkstationApiClient(changingTransport);
    var enrolled = await changingApi.RegisterAsync(moving, CancellationToken.None);
    Assert(enrolled.ServiceEndpoint == primary.BaseUrl + "/", "pairing is pinned to the actual request endpoint across settings changes");
}
var traversalRejected = false;
try { WorkstationServiceEndpoint.BuildUrl(new Uri(primary.BaseUrl), "api/workstation-local/../../admin"); }
catch (ArgumentException) { traversalRejected = true; }
Assert(traversalRejected, "document routes cannot traverse outside the workstation API");
Console.WriteLine($"ENDPOINT_REGRESSION_RESULT passed={passed}");

internal sealed class ChangingRegistrationHandler(Action change) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        change();
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"ok\":true,\"workstation\":{\"device_token\":\"fixture\"}}", Encoding.UTF8, "application/json")
        });
    }
}

internal sealed record RecordedRequest(string Path, Dictionary<string,string> Headers);
internal sealed class SyntheticService : IAsyncDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource stop = new();
    private readonly Task loop;
    public ConcurrentQueue<RecordedRequest> Requests { get; } = new();
    public string? RedirectJobsTo { get; set; }
    public string BaseUrl { get; }
    public SyntheticService()
    {
        listener.Start();
        BaseUrl = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port;
        loop = RunAsync();
    }
    private async Task RunAsync()
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                using var client = await listener.AcceptTcpClientAsync(stop.Token);
                using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.UTF8, false, 1024, leaveOpen:true);
                var line = await reader.ReadLineAsync(stop.Token);
                if (string.IsNullOrWhiteSpace(line)) continue;
                var target = line.Split(' ')[1];
                var path = new Uri(BaseUrl + target).AbsolutePath;
                var headers = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
                while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync(stop.Token)))
                {
                    var colon = line.IndexOf(':');
                    if (colon > 0) headers[line.Substring(0,colon)] = line.Substring(colon+1).Trim();
                }
                if (headers.TryGetValue("Content-Length", out var length) && int.TryParse(length, out var size) && size > 0)
                {
                    if (size > 16384) throw new InvalidOperationException("Unexpected fixture body size.");
                    var body = new char[size];
                    await reader.ReadBlockAsync(body.AsMemory(), stop.Token);
                }
                Requests.Enqueue(new(path, headers));
                var redirect = path.EndsWith("/work-orders") ? RedirectJobsTo : null;
                var json = path.EndsWith("/register")
                    ? "{\"ok\":true,\"workstation\":{\"id\":\"kiosk-1\",\"shop_id\":\"shop-1\",\"device_token\":\"device-secret\",\"is_active\":true,\"status\":\"active\"}}"
                    : path.EndsWith("/login")
                      ? "{\"ok\":true,\"session\":{\"token\":\"employee-secret\"},\"payload\":{}}"
                      : "{\"ok\":true}";
                var bytes = Encoding.UTF8.GetBytes(json);
                var header = redirect == null ? "HTTP/1.1 200 OK\r\n" : "HTTP/1.1 302 Found\r\nLocation: " + redirect + "\r\n";
                header += "Content-Type: application/json\r\nConnection: close\r\nContent-Length: " + bytes.Length + "\r\n\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(header), stop.Token);
                await stream.WriteAsync(bytes, stop.Token);
                await stream.FlushAsync(stop.Token);
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (SocketException) when (stop.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (stop.IsCancellationRequested) { }
    }
    public async ValueTask DisposeAsync()
    {
        stop.Cancel(); listener.Stop();
        await loop; stop.Dispose();
    }
}
