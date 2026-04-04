using RunBook.Workstation.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace RunBook.Workstation.Services
{
    public sealed class RunBookWorkstationApiClient
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly HttpClient _client;

        public RunBookWorkstationApiClient(HttpClient? client = null)
        {
            _client = client ?? new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        }

        public async Task<WorkstationRegistrationSnapshot> RegisterAsync(WorkstationSettings settings, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, BuildUrl(settings.DesktopBaseUrl, "api/workstation-local/register"));
            request.Content = new StringContent(JsonSerializer.Serialize(new
            {
                shop_id = settings.ShopId,
                workstation_id = settings.WorkstationId,
                workstation_name = settings.WorkstationName,
                pairing_code = settings.PairingCode,
            }), Encoding.UTF8, "application/json");

            using var response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var payload = await DeserializeAsync<RegisterResponse>(response, cancellationToken).ConfigureAwait(false);
            EnsureSuccess(response, payload?.Error, "Workstation local registration failed.");

            return new WorkstationRegistrationSnapshot
            {
                WorkstationId = payload?.Workstation?.Id ?? settings.WorkstationId,
                ShopId = payload?.Workstation?.ShopId ?? settings.ShopId,
                WorkstationName = payload?.Workstation?.Name ?? settings.WorkstationName,
                Status = payload?.Workstation?.Status ?? "active",
                DeviceToken = payload?.Workstation?.DeviceToken ?? "",
                TokenIssuedUtc = payload?.Workstation?.TokenIssuedUtc ?? DateTime.UtcNow.ToString("O"),
                IsActive = payload?.Workstation?.IsActive ?? true,
                EnrollmentVersion = payload?.Workstation?.EnrollmentVersion ?? 1,
                Existing = payload?.Existing ?? false,
                LastSyncUtc = DateTime.UtcNow.ToString("O"),
            };
        }

        public async Task<LoginResponse> LoginAsync(WorkstationSettings settings, string passcode, CancellationToken cancellationToken)
        {
            using var request = BuildBearerRequest(HttpMethod.Post, settings, "api/workstation/login", new
            {
                shop_id = settings.ShopId,
                workstation_id = settings.WorkstationId,
                passcode,
            });

            using var response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var payload = await DeserializeAsync<LoginResponse>(response, cancellationToken).ConfigureAwait(false);
            EnsureSuccess(response, payload?.Error, "Workstation login failed.");
            return payload ?? throw new InvalidOperationException("Empty login response.");
        }

        public async Task<EmployeeDirectoryResponse> GetEmployeeDirectoryAsync(WorkstationSettings settings, CancellationToken cancellationToken)
        {
            var url = BuildUrl(settings.ControlBaseUrl, $"api/desktop/employee-directory?shop_id={Uri.EscapeDataString(settings.ShopId ?? string.Empty)}");
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ControlSessionService.GetAccessToken(settings.ControlBaseUrl));

            using var response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var payload = await DeserializeAsync<EmployeeDirectoryResponse>(response, cancellationToken).ConfigureAwait(false);
            EnsureSuccess(response, payload?.Error, "Unable to load employee directory.");
            return payload ?? throw new InvalidOperationException("Empty employee directory response.");
        }

        public async Task<LocalAuthPackageResponse> GetLocalAuthPackageAsync(WorkstationSettings settings, CancellationToken cancellationToken)
        {
            var context = GetLocalDesktopContext(settings);
            var url = BuildUrl(settings.DesktopBaseUrl, $"api/workstation-local/auth-package?shop_id={Uri.EscapeDataString(context.ShopId)}&workstation_id={Uri.EscapeDataString(context.WorkstationId)}");
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            AddLocalDeviceAuthorization(request, settings);
            using var response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var payload = await DeserializeAsync<LocalAuthPackageResponse>(response, cancellationToken).ConfigureAwait(false);
            EnsureSuccess(response, payload?.Error, "Unable to load Desktop employee auth package.");
            return payload ?? throw new InvalidOperationException("Empty auth package response.");
        }

        public async Task<LocalEmployeeSessionResponse> LoginLocalEmployeeAsync(
            WorkstationSettings settings,
            string remoteEmployeeId,
            string employeeId,
            string employeeCode,
            string passcode,
            CancellationToken cancellationToken)
        {
            var context = GetLocalDesktopContext(settings);
            using var request = new HttpRequestMessage(HttpMethod.Post, BuildUrl(settings.DesktopBaseUrl, "api/workstation-local/login"));
            AddLocalDeviceAuthorization(request, settings);
            request.Content = new StringContent(JsonSerializer.Serialize(new
            {
                shop_id = context.ShopId,
                workstation_id = context.WorkstationId,
                remote_employee_id = remoteEmployeeId,
                employee_id = employeeId,
                employee_code = employeeCode,
                passcode
            }, JsonOptions), Encoding.UTF8, "application/json");

            using var response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var payload = await DeserializeAsync<LocalEmployeeSessionResponse>(response, cancellationToken).ConfigureAwait(false);
            EnsureSuccess(response, payload?.Error, "Unable to sign in to Desktop.");
            if (payload?.Session == null || payload.Payload == null || string.IsNullOrWhiteSpace(payload.Session.Token))
                throw new InvalidOperationException("Desktop did not return a valid workstation sign-in session.");

            return payload;
        }

        public async Task<CurrentShopResponse> GetCurrentShopAsync(string baseUrl, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, BuildUrl(baseUrl, "api/desktop/current-shop"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ControlSessionService.GetAccessToken(baseUrl));

            using var response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var payload = await DeserializeAsync<CurrentShopResponse>(response, cancellationToken).ConfigureAwait(false);
            EnsureSuccess(response, payload?.Error, "Unable to load current shop.");
            return payload ?? throw new InvalidOperationException("Empty current shop response.");
        }

        public async Task<DesktopTimeclockStateResponse> GetDesktopTimeclockStateAsync(WorkstationSettings settings, WorkstationSessionSnapshot session, CancellationToken cancellationToken)
        {
            var url = BuildLocalSessionUrl(settings, "api/workstation-local/timeclock/state");
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            AddLocalDeviceAuthorization(request, settings);
            AddLocalEmployeeSession(request, session);
            using var response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var payload = await DeserializeAsync<DesktopTimeclockStateResponse>(response, cancellationToken).ConfigureAwait(false);
            EnsureSuccess(response, payload?.Error, "Unable to load Desktop time clock state.");
            return payload ?? throw new InvalidOperationException("Empty Desktop time clock response.");
        }

        public async Task<DesktopSyncResponse> SyncDesktopTimeclockAsync(WorkstationSettings settings, WorkstationSessionSnapshot session, IEnumerable<WorkstationSyncQueueItem> items, CancellationToken cancellationToken)
        {
            var body = new DesktopSyncRequest
            {
                ShopId = settings.ShopId,
                WorkstationId = settings.WorkstationId,
                Items = items?.Select(MapSyncItem).ToList() ?? new List<DesktopSyncItem>()
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, BuildUrl(settings.DesktopBaseUrl, "api/workstation-local/timeclock/sync"));
            AddLocalDeviceAuthorization(request, settings);
            AddLocalEmployeeSession(request, session);
            request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
            using var response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var payload = await DeserializeAsync<DesktopSyncResponse>(response, cancellationToken).ConfigureAwait(false);
            EnsureSuccess(response, payload?.Error, "Unable to sync Desktop time clock items.");
            return payload ?? throw new InvalidOperationException("Empty Desktop sync response.");
        }

        public async Task<CapabilityPayloadResponse> GetSessionMeAsync(WorkstationSettings settings, WorkstationSessionSnapshot session, CancellationToken cancellationToken)
        {
            var url = BuildLocalSessionUrl(settings, "api/workstation-local/session/me");
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            AddLocalDeviceAuthorization(request, settings);
            AddLocalEmployeeSession(request, session);
            using var response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var payload = await DeserializeAsync<CapabilityPayloadResponse>(response, cancellationToken).ConfigureAwait(false);
            EnsureSuccess(response, payload?.Error, "Unable to load workstation session.");
            return payload ?? throw new InvalidOperationException("Empty workstation session response.");
        }

        public async Task<CapabilityPayloadResponse> GetNavigationAsync(WorkstationSettings settings, WorkstationSessionSnapshot session, CancellationToken cancellationToken)
        {
            var url = BuildLocalSessionUrl(settings, "api/workstation-local/navigation");
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            AddLocalDeviceAuthorization(request, settings);
            AddLocalEmployeeSession(request, session);
            using var response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var payload = await DeserializeAsync<CapabilityPayloadResponse>(response, cancellationToken).ConfigureAwait(false);
            EnsureSuccess(response, payload?.Error, "Unable to load workstation navigation.");
            return payload ?? throw new InvalidOperationException("Empty workstation navigation response.");
        }

        public async Task<WorkOrderListResponse> GetWorkOrdersAsync(WorkstationSettings settings, WorkstationSessionSnapshot session, CancellationToken cancellationToken)
        {
            var url = BuildLocalSessionUrl(settings, "api/workstation-local/work-orders");
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            AddLocalDeviceAuthorization(request, settings);
            AddLocalEmployeeSession(request, session);
            using var response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var payload = await DeserializeAsync<WorkOrderListResponse>(response, cancellationToken).ConfigureAwait(false);
            EnsureSuccess(response, payload?.Error, "Unable to load work orders.");
            return payload ?? throw new InvalidOperationException("Empty work order list response.");
        }

        public async Task<WorkOrderDetailResponse> GetWorkOrderDetailAsync(WorkstationSettings settings, WorkstationSessionSnapshot session, int workOrderId, CancellationToken cancellationToken)
        {
            var url = BuildLocalSessionUrl(settings, $"api/workstation-local/work-orders/{workOrderId}");
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            AddLocalDeviceAuthorization(request, settings);
            AddLocalEmployeeSession(request, session);
            using var response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var payload = await DeserializeAsync<WorkOrderDetailResponse>(response, cancellationToken).ConfigureAwait(false);
            EnsureSuccess(response, payload?.Error, "Unable to load work order detail.");
            return payload ?? throw new InvalidOperationException("Empty work order detail response.");
        }

        public async Task<DrawingPackageResponse> GetWorkOrderDrawingsAsync(WorkstationSettings settings, WorkstationSessionSnapshot session, int workOrderId, CancellationToken cancellationToken)
        {
            var url = BuildLocalSessionUrl(settings, $"api/workstation-local/work-orders/{workOrderId}/drawings");
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            AddLocalDeviceAuthorization(request, settings);
            AddLocalEmployeeSession(request, session);
            using var response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var payload = await DeserializeAsync<DrawingPackageResponse>(response, cancellationToken).ConfigureAwait(false);
            EnsureSuccess(response, payload?.Error, "Unable to load work order drawings.");
            return payload ?? throw new InvalidOperationException("Empty drawing package response.");
        }

        public async Task<DrawingContentResponse> DownloadDrawingAsync(WorkstationSettings settings, WorkstationSessionSnapshot session, string relativeRoute, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, BuildLocalSessionUrl(settings, relativeRoute));
            AddLocalDeviceAuthorization(request, settings);
            AddLocalEmployeeSession(request, session);
            using var response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var payload = await DeserializeAsync<DrawingContentResponse>(response, cancellationToken).ConfigureAwait(false);
            EnsureSuccess(response, payload?.Error, "Unable to download drawing.");
            return payload ?? throw new InvalidOperationException("Empty drawing download response.");
        }

        public async Task<WorkOrderDetailResponse> StartOperationAsync(WorkstationSettings settings, WorkstationSessionSnapshot session, int workOrderId, int operationId, string note, CancellationToken cancellationToken)
            => await PostWorkOrderOperationAsync(settings, session, $"api/workstation-local/work-orders/{workOrderId}/operations/{operationId}/start", new { note }, cancellationToken).ConfigureAwait(false);

        public async Task<WorkOrderDetailResponse> StopOperationAsync(WorkstationSettings settings, WorkstationSessionSnapshot session, int workOrderId, int operationId, string note, CancellationToken cancellationToken)
            => await PostWorkOrderOperationAsync(settings, session, $"api/workstation-local/work-orders/{workOrderId}/operations/{operationId}/stop", new { note }, cancellationToken).ConfigureAwait(false);

        public async Task<WorkOrderDetailResponse> CompleteOperationAsync(WorkstationSettings settings, WorkstationSessionSnapshot session, int workOrderId, int operationId, string note, CancellationToken cancellationToken)
            => await PostWorkOrderOperationAsync(settings, session, $"api/workstation-local/work-orders/{workOrderId}/operations/{operationId}/complete", new { note }, cancellationToken).ConfigureAwait(false);

        public async Task<WorkOrderDetailResponse> ReportQuantityAsync(WorkstationSettings settings, WorkstationSessionSnapshot session, int workOrderId, int operationId, int quantity, string note, CancellationToken cancellationToken)
            => await PostWorkOrderOperationAsync(settings, session, $"api/workstation-local/work-orders/{workOrderId}/report-quantity", new { operation_id = operationId, quantity, note }, cancellationToken).ConfigureAwait(false);

        public async Task<WorkOrderDetailResponse> ReportScrapAsync(WorkstationSettings settings, WorkstationSessionSnapshot session, int workOrderId, int operationId, int quantity, string note, CancellationToken cancellationToken)
            => await PostWorkOrderOperationAsync(settings, session, $"api/workstation-local/work-orders/{workOrderId}/report-scrap", new { operation_id = operationId, quantity, note }, cancellationToken).ConfigureAwait(false);

        public async Task<WorkOrderDetailResponse> AddOperationNoteAsync(WorkstationSettings settings, WorkstationSessionSnapshot session, int workOrderId, int operationId, string note, CancellationToken cancellationToken)
            => await PostWorkOrderOperationAsync(settings, session, $"api/workstation-local/work-orders/{workOrderId}/notes", new { operation_id = operationId, note }, cancellationToken).ConfigureAwait(false);

        public async Task<InspectionTaskResponse> GetInspectionTasksAsync(WorkstationSettings settings, WorkstationSessionSnapshot session, int workOrderId, int operationId, CancellationToken cancellationToken)
        {
            var relative = operationId > 0
                ? $"api/workstation-local/work-orders/{workOrderId}/inspection-tasks?operation_id={operationId}"
                : $"api/workstation-local/work-orders/{workOrderId}/inspection-tasks";
            using var request = new HttpRequestMessage(HttpMethod.Get, BuildLocalSessionUrl(settings, relative));
            AddLocalDeviceAuthorization(request, settings);
            AddLocalEmployeeSession(request, session);
            using var response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var payload = await DeserializeAsync<InspectionTaskResponse>(response, cancellationToken).ConfigureAwait(false);
            EnsureSuccess(response, payload?.Error, "Unable to load workstation inspection tasks.");
            return payload ?? throw new InvalidOperationException("Empty inspection task response.");
        }

        public async Task<InspectionTaskResponse> SubmitInspectionResultAsync(WorkstationSettings settings, WorkstationSessionSnapshot session, int workOrderId, int operationId, int featureSetId, int featureId, int sampleIndex, string actualValue, string notes, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, BuildLocalSessionUrl(settings, "api/workstation-local/inspection-results"));
            AddLocalDeviceAuthorization(request, settings);
            AddLocalEmployeeSession(request, session);
            request.Content = new StringContent(JsonSerializer.Serialize(new
            {
                work_order_id = workOrderId,
                operation_id = operationId,
                feature_set_id = featureSetId,
                feature_id = featureId,
                sample_index = sampleIndex,
                actual_value = actualValue,
                notes
            }, JsonOptions), Encoding.UTF8, "application/json");
            using var response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var payload = await DeserializeAsync<InspectionTaskResponse>(response, cancellationToken).ConfigureAwait(false);
            EnsureSuccess(response, payload?.Error, "Unable to submit workstation inspection result.");
            return payload ?? throw new InvalidOperationException("Empty inspection submit response.");
        }

        private async Task<WorkOrderDetailResponse> PostWorkOrderOperationAsync(WorkstationSettings settings, WorkstationSessionSnapshot session, string relativeUrl, object body, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, BuildLocalSessionUrl(settings, relativeUrl));
            AddLocalDeviceAuthorization(request, settings);
            AddLocalEmployeeSession(request, session);
            request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
            using var response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var payload = await DeserializeAsync<WorkOrderDetailResponse>(response, cancellationToken).ConfigureAwait(false);
            EnsureSuccess(response, payload?.Error, "Unable to save workstation work-order action.");
            return payload ?? throw new InvalidOperationException("Empty workstation work-order action response.");
        }

        private static DesktopSyncItem MapSyncItem(WorkstationSyncQueueItem item)
        {
            return new DesktopSyncItem
            {
                QueueId = item.QueueId,
                ClientEventId = item.ClientEventId,
                ItemType = item.ItemType,
                RemoteEmployeeId = item.RemoteEmployeeId,
                EmployeeId = item.EmployeeId,
                EmployeeCode = item.EmployeeCode,
                EmployeeName = item.EmployeeName,
                ShopId = item.ShopId,
                WorkstationId = item.WorkstationId,
                DeviceId = item.DeviceId,
                ActionType = item.ActionType,
                EffectiveUtc = item.EffectiveUtc,
                CreatedLocalUtc = item.CreatedLocalUtc,
                Note = item.Note,
                RequestType = item.RequestType,
                StartDate = item.StartDate,
                EndDate = item.EndDate,
                HoursRequested = item.HoursRequested,
                WorkOrderId = item.WorkOrderId,
                WorkOrderNumber = item.WorkOrderNumber,
                OperationId = item.OperationId,
                OperationNumber = item.OperationNumber,
                OperationTitle = item.OperationTitle,
            };
        }

        private static HttpRequestMessage BuildBearerRequest(HttpMethod method, WorkstationSettings settings, string relativeUrl, object body)
        {
            var request = new HttpRequestMessage(method, BuildUrl(settings.ControlBaseUrl, relativeUrl));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ControlSessionService.GetAccessToken(settings.ControlBaseUrl));
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            return request;
        }

        private static string BuildLocalSessionUrl(WorkstationSettings settings, string relativeUrl)
        {
            var context = GetLocalDesktopContext(settings);
            var query = $"shop_id={Uri.EscapeDataString(context.ShopId)}&workstation_id={Uri.EscapeDataString(context.WorkstationId)}";
            var separator = relativeUrl.Contains('?') ? "&" : "?";
            return BuildUrl(settings.DesktopBaseUrl, $"{relativeUrl}{separator}{query}");
        }

        private static void AddLocalDeviceAuthorization(HttpRequestMessage request, WorkstationSettings settings)
        {
            var registration = WorkstationStorageService.LoadRegistration();
            if (registration == null || string.IsNullOrWhiteSpace(registration.DeviceToken))
                throw new InvalidOperationException("Workstation is not enrolled with Desktop yet.");

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", registration.DeviceToken);
            request.Headers.TryAddWithoutValidation("X-RunBook-Workstation-Id", registration.WorkstationId ?? settings.WorkstationId ?? "");
        }

        private static (string ShopId, string WorkstationId) GetLocalDesktopContext(WorkstationSettings settings)
        {
            var registration = WorkstationStorageService.LoadRegistration();
            var shopId = !string.IsNullOrWhiteSpace(registration?.ShopId)
                ? (registration?.ShopId ?? "").Trim()
                : (settings.ShopId ?? "").Trim();
            var workstationId = !string.IsNullOrWhiteSpace(registration?.WorkstationId)
                ? (registration?.WorkstationId ?? "").Trim()
                : (settings.WorkstationId ?? "").Trim();
            return (shopId, workstationId);
        }

        private static void AddLocalEmployeeSession(HttpRequestMessage request, WorkstationSessionSnapshot session)
        {
            if (session == null || string.IsNullOrWhiteSpace(session.Token))
                throw new InvalidOperationException("Workstation sign-in is required.");

            request.Headers.TryAddWithoutValidation("X-RunBook-Employee-Session", session.Token);
        }

        private static string BuildUrl(string baseUrl, string relativeUrl)
            => $"{(baseUrl ?? string.Empty).TrimEnd('/')}/{relativeUrl.TrimStart('/')}";

        private static async Task<T?> DeserializeAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(text))
                return default;
            return JsonSerializer.Deserialize<T>(text, JsonOptions);
        }

        private static void EnsureSuccess(HttpResponseMessage response, string? apiError, string fallbackMessage)
        {
            if (response.IsSuccessStatusCode)
                return;

            throw new InvalidOperationException(string.IsNullOrWhiteSpace(apiError) ? fallbackMessage : apiError);
        }

        public sealed class RegisterResponse
        {
            [JsonPropertyName("ok")]
            public bool Ok { get; set; }
            [JsonPropertyName("error")]
            public string? Error { get; set; }
            [JsonPropertyName("existing")]
            public bool Existing { get; set; }
            [JsonPropertyName("workstation")]
            public RegisterWorkstation? Workstation { get; set; }
        }

        public sealed class RegisterWorkstation
        {
            [JsonPropertyName("id")]
            public string Id { get; set; } = "";
            [JsonPropertyName("shop_id")]
            public string ShopId { get; set; } = "";
            [JsonPropertyName("name")]
            public string Name { get; set; } = "";
            [JsonPropertyName("status")]
            public string Status { get; set; } = "";
            [JsonPropertyName("device_token")]
            public string DeviceToken { get; set; } = "";
            [JsonPropertyName("token_issued_utc")]
            public string TokenIssuedUtc { get; set; } = "";
            [JsonPropertyName("is_active")]
            public bool IsActive { get; set; } = true;
            [JsonPropertyName("enrollment_version")]
            public int EnrollmentVersion { get; set; } = 1;
        }

        public sealed class LoginResponse
        {
            [JsonPropertyName("ok")]
            public bool Ok { get; set; }
            [JsonPropertyName("error")]
            public string? Error { get; set; }
            [JsonPropertyName("employee")]
            public LoginEmployee? Employee { get; set; }
            [JsonPropertyName("capabilities")]
            public LoginCapabilities? Capabilities { get; set; }
            [JsonPropertyName("session")]
            public LoginSession? Session { get; set; }
            [JsonPropertyName("workstation")]
            public RegisterWorkstation? Workstation { get; set; }
            [JsonPropertyName("recent_punches")]
            public List<TimeClockPunch>? RecentPunches { get; set; }
        }

        public sealed class LoginEmployee
        {
            [JsonPropertyName("id")]
            public string Id { get; set; } = "";
            [JsonPropertyName("shop_id")]
            public string ShopId { get; set; } = "";
            [JsonPropertyName("employee_code")]
            public string EmployeeCode { get; set; } = "";
            [JsonPropertyName("display_name")]
            public string DisplayName { get; set; } = "";
            [JsonPropertyName("role")]
            public string Role { get; set; } = "";
        }

        public sealed class LoginCapabilities
        {
            [JsonPropertyName("modules")]
            public List<string> Modules { get; set; } = new List<string>();
            [JsonPropertyName("session_timeout_minutes")]
            public int SessionTimeoutMinutes { get; set; }
        }

        public sealed class LoginSession
        {
            [JsonPropertyName("token")]
            public string Token { get; set; } = "";
            [JsonPropertyName("issued_at_utc")]
            public string IssuedAtUtc { get; set; } = "";
            [JsonPropertyName("expires_at_utc")]
            public string ExpiresAtUtc { get; set; } = "";
        }

        public sealed class EmployeeDirectoryResponse
        {
            [JsonPropertyName("ok")]
            public bool Ok { get; set; }

            [JsonPropertyName("error")]
            public string? Error { get; set; }

            [JsonPropertyName("employees")]
            public List<EmployeeDirectoryEmployee> Employees { get; set; } = new List<EmployeeDirectoryEmployee>();
        }

        public sealed class CurrentShopResponse
        {
            [JsonPropertyName("ok")]
            public bool Ok { get; set; }

            [JsonPropertyName("error")]
            public string? Error { get; set; }

            [JsonPropertyName("found")]
            public bool Found { get; set; }

            [JsonPropertyName("multiple_shops")]
            public bool MultipleShops { get; set; }

            [JsonPropertyName("shop_id")]
            public string ShopId { get; set; } = "";

            [JsonPropertyName("shop_name")]
            public string ShopName { get; set; } = "";

            [JsonPropertyName("member_role")]
            public string MemberRole { get; set; } = "";
        }

        public sealed class LocalAuthPackageResponse
        {
            [JsonPropertyName("ok")]
            public bool Ok { get; set; }

            [JsonPropertyName("error")]
            public string? Error { get; set; }

            [JsonPropertyName("package")]
            public LocalAuthPackage? Package { get; set; }
        }

        public sealed class LocalEmployeeSessionResponse
        {
            [JsonPropertyName("ok")]
            public bool Ok { get; set; }

            [JsonPropertyName("error")]
            public string? Error { get; set; }

            [JsonPropertyName("session")]
            public LocalEmployeeSession? Session { get; set; }

            [JsonPropertyName("payload")]
            public CapabilityPayload? Payload { get; set; }
        }

        public sealed class LocalEmployeeSession
        {
            [JsonPropertyName("token")]
            public string Token { get; set; } = "";

            [JsonPropertyName("issued_at_utc")]
            public string IssuedAtUtc { get; set; } = "";

            [JsonPropertyName("expires_at_utc")]
            public string ExpiresAtUtc { get; set; } = "";

            [JsonPropertyName("session_timeout_minutes")]
            public int SessionTimeoutMinutes { get; set; } = 15;
        }

        public sealed class LocalAuthPackage
        {
            [JsonPropertyName("shop_id")]
            public string ShopId { get; set; } = "";

            [JsonPropertyName("workstation_id")]
            public string WorkstationId { get; set; } = "";

            [JsonPropertyName("auth_package_version")]
            public int AuthPackageVersion { get; set; } = 1;

            [JsonPropertyName("package_hash")]
            public string PackageHash { get; set; } = "";

            [JsonPropertyName("last_synced_utc")]
            public string LastSyncedUtc { get; set; } = "";

            [JsonPropertyName("source_state")]
            public string SourceState { get; set; } = "";

            [JsonPropertyName("policy")]
            public LocalAuthPolicy Policy { get; set; } = new LocalAuthPolicy();

            [JsonPropertyName("employees")]
            public List<LocalAuthEmployee> Employees { get; set; } = new List<LocalAuthEmployee>();
        }

        public sealed class LocalAuthPolicy
        {
            [JsonPropertyName("warning_after_hours")]
            public int WarningAfterHours { get; set; } = 24;

            [JsonPropertyName("hard_stop_after_hours")]
            public int HardStopAfterHours { get; set; } = 168;
        }

        public sealed class EmployeeDirectoryEmployee
        {
            [JsonPropertyName("id")]
            public string Id { get; set; } = "";

            [JsonPropertyName("employee_code")]
            public string EmployeeCode { get; set; } = "";

            [JsonPropertyName("display_name")]
            public string DisplayName { get; set; } = "";

            [JsonPropertyName("avatar_display_url_256")]
            public string AvatarDisplayUrl256 { get; set; } = "";

            [JsonPropertyName("role")]
            public string Role { get; set; } = "";

            [JsonPropertyName("is_active")]
            public bool IsActive { get; set; }

            [JsonPropertyName("workstation_access_enabled")]
            public bool WorkstationAccessEnabled { get; set; }

            [JsonPropertyName("can_timeclock")]
            public bool CanTimeclock { get; set; }

            [JsonPropertyName("can_dashboard_view")]
            public bool CanDashboardView { get; set; }

            [JsonPropertyName("can_jobs_module")]
            public bool CanJobsModule { get; set; }

            [JsonPropertyName("can_inspection_entry")]
            public bool CanInspectionEntry { get; set; }

            [JsonPropertyName("can_camera_view")]
            public bool CanCameraView { get; set; }
        }

        public sealed class LocalAuthEmployee
        {
            [JsonPropertyName("employee_id")]
            public string EmployeeId { get; set; } = "";

            [JsonPropertyName("remote_employee_id")]
            public string RemoteEmployeeId { get; set; } = "";

            [JsonPropertyName("employee_code")]
            public string EmployeeCode { get; set; } = "";

            [JsonPropertyName("display_name")]
            public string DisplayName { get; set; } = "";

            [JsonPropertyName("role")]
            public string Role { get; set; } = "";

            [JsonPropertyName("status")]
            public string Status { get; set; } = "";

            [JsonPropertyName("is_active")]
            public bool IsActive { get; set; }

            [JsonPropertyName("workstation_access_enabled")]
            public bool WorkstationAccessEnabled { get; set; }

            [JsonPropertyName("can_timeclock")]
            public bool CanTimeClock { get; set; }

            [JsonPropertyName("can_dashboard_view")]
            public bool CanDashboardView { get; set; }

            [JsonPropertyName("can_jobs_module")]
            public bool CanJobsModule { get; set; }

            [JsonPropertyName("can_inspection_entry")]
            public bool CanInspectionEntry { get; set; }

            [JsonPropertyName("can_camera_view")]
            public bool CanCameraView { get; set; }

            [JsonPropertyName("has_workstation_passcode")]
            public bool HasWorkstationPasscode { get; set; }

            [JsonPropertyName("session_timeout_minutes")]
            public int SessionTimeoutMinutes { get; set; } = 15;

            [JsonPropertyName("avatar_display_url")]
            public string AvatarDisplayUrl { get; set; } = "";

            [JsonPropertyName("updated_utc")]
            public string UpdatedUtc { get; set; } = "";
        }

        public sealed class TimeClockPunch
        {
            [JsonPropertyName("id")]
            public string Id { get; set; } = "";
            [JsonPropertyName("event_type")]
            public string EventType { get; set; } = "";
            [JsonPropertyName("client_ts")]
            public string ClientTs { get; set; } = "";
            [JsonPropertyName("server_ts")]
            public string ServerTs { get; set; } = "";
            [JsonPropertyName("source")]
            public string Source { get; set; } = "";
            [JsonPropertyName("note")]
            public string Note { get; set; } = "";
            [JsonPropertyName("is_pending_sync")]
            public bool IsPendingSync { get; set; }
            [JsonPropertyName("sync_state")]
            public string SyncState { get; set; } = "";
        }

        public sealed class DesktopTimeclockStateResponse
        {
            [JsonPropertyName("ok")]
            public bool Ok { get; set; }
            [JsonPropertyName("error")]
            public string? Error { get; set; }
            [JsonPropertyName("snapshot")]
            public DesktopTimeclockSnapshot? Snapshot { get; set; }
        }

        public sealed class DesktopTimeclockSnapshot
        {
            [JsonPropertyName("remote_employee_id")]
            public string RemoteEmployeeId { get; set; } = "";
            [JsonPropertyName("employee_id")]
            public string EmployeeId { get; set; } = "";
            [JsonPropertyName("employee_code")]
            public string EmployeeCode { get; set; } = "";
            [JsonPropertyName("employee_name")]
            public string EmployeeName { get; set; } = "";
            [JsonPropertyName("current_status")]
            public string CurrentStatus { get; set; } = "OUT";
            [JsonPropertyName("status_since_utc")]
            public string StatusSinceUtc { get; set; } = "";
            [JsonPropertyName("last_punch_utc")]
            public string LastPunchUtc { get; set; } = "";
            [JsonPropertyName("supports_lunch")]
            public bool SupportsLunch { get; set; }
            [JsonPropertyName("last_sync_utc")]
            public string LastSyncUtc { get; set; } = "";
            [JsonPropertyName("last_sync_message")]
            public string LastSyncMessage { get; set; } = "";
            [JsonPropertyName("recent_punches")]
            public List<TimeClockPunch> RecentPunches { get; set; } = new List<TimeClockPunch>();
            [JsonPropertyName("recent_time_off_requests")]
            public List<DesktopTimeOffRequest> RecentTimeOffRequests { get; set; } = new List<DesktopTimeOffRequest>();
        }

        public sealed class DesktopTimeOffRequest
        {
            [JsonPropertyName("id")]
            public string Id { get; set; } = "";
            [JsonPropertyName("request_type")]
            public string RequestType { get; set; } = "";
            [JsonPropertyName("start_date")]
            public string StartDate { get; set; } = "";
            [JsonPropertyName("end_date")]
            public string EndDate { get; set; } = "";
            [JsonPropertyName("hours_requested")]
            public decimal? HoursRequested { get; set; }
            [JsonPropertyName("work_order_id")]
            public int WorkOrderId { get; set; }
            [JsonPropertyName("work_order_number")]
            public string WorkOrderNumber { get; set; } = "";
            [JsonPropertyName("operation_id")]
            public int OperationId { get; set; }
            [JsonPropertyName("operation_number")]
            public int OperationNumber { get; set; }
            [JsonPropertyName("operation_title")]
            public string OperationTitle { get; set; } = "";
            [JsonPropertyName("employee_note")]
            public string EmployeeNote { get; set; } = "";
            [JsonPropertyName("manager_note")]
            public string ManagerNote { get; set; } = "";
            [JsonPropertyName("status")]
            public string Status { get; set; } = "";
            [JsonPropertyName("submitted_utc")]
            public string SubmittedUtc { get; set; } = "";
            [JsonPropertyName("is_pending_sync")]
            public bool IsPendingSync { get; set; }
            [JsonPropertyName("sync_state")]
            public string SyncState { get; set; } = "";
        }

        public sealed class DesktopSyncRequest
        {
            [JsonPropertyName("shop_id")]
            public string ShopId { get; set; } = "";
            [JsonPropertyName("workstation_id")]
            public string WorkstationId { get; set; } = "";
            [JsonPropertyName("employee_id")]
            public string EmployeeId { get; set; } = "";
            [JsonPropertyName("employee_code")]
            public string EmployeeCode { get; set; } = "";
            [JsonPropertyName("employee_name")]
            public string EmployeeName { get; set; } = "";
            [JsonPropertyName("items")]
            public List<DesktopSyncItem> Items { get; set; } = new List<DesktopSyncItem>();
        }

        public sealed class DesktopSyncItem
        {
            [JsonPropertyName("queue_id")]
            public string QueueId { get; set; } = "";
            [JsonPropertyName("client_event_id")]
            public string ClientEventId { get; set; } = "";
            [JsonPropertyName("item_type")]
            public string ItemType { get; set; } = "";
            [JsonPropertyName("remote_employee_id")]
            public string RemoteEmployeeId { get; set; } = "";
            [JsonPropertyName("employee_id")]
            public string EmployeeId { get; set; } = "";
            [JsonPropertyName("employee_code")]
            public string EmployeeCode { get; set; } = "";
            [JsonPropertyName("employee_name")]
            public string EmployeeName { get; set; } = "";
            [JsonPropertyName("shop_id")]
            public string ShopId { get; set; } = "";
            [JsonPropertyName("workstation_id")]
            public string WorkstationId { get; set; } = "";
            [JsonPropertyName("device_id")]
            public string DeviceId { get; set; } = "";
            [JsonPropertyName("action_type")]
            public string ActionType { get; set; } = "";
            [JsonPropertyName("effective_utc")]
            public string EffectiveUtc { get; set; } = "";
            [JsonPropertyName("created_local_utc")]
            public string CreatedLocalUtc { get; set; } = "";
            [JsonPropertyName("note")]
            public string Note { get; set; } = "";
            [JsonPropertyName("request_type")]
            public string RequestType { get; set; } = "";
            [JsonPropertyName("start_date")]
            public string StartDate { get; set; } = "";
            [JsonPropertyName("end_date")]
            public string EndDate { get; set; } = "";
            [JsonPropertyName("hours_requested")]
            public decimal? HoursRequested { get; set; }
            [JsonPropertyName("work_order_id")]
            public int WorkOrderId { get; set; }
            [JsonPropertyName("work_order_number")]
            public string WorkOrderNumber { get; set; } = "";
            [JsonPropertyName("operation_id")]
            public int OperationId { get; set; }
            [JsonPropertyName("operation_number")]
            public int OperationNumber { get; set; }
            [JsonPropertyName("operation_title")]
            public string OperationTitle { get; set; } = "";
        }

        public sealed class DesktopSyncResponse
        {
            [JsonPropertyName("ok")]
            public bool Ok { get; set; }
            [JsonPropertyName("error")]
            public string? Error { get; set; }
            [JsonPropertyName("results")]
            public List<DesktopSyncItemResult> Results { get; set; } = new List<DesktopSyncItemResult>();
            [JsonPropertyName("snapshot")]
            public DesktopTimeclockSnapshot? Snapshot { get; set; }
        }

        public sealed class DesktopSyncItemResult
        {
            [JsonPropertyName("queue_id")]
            public string QueueId { get; set; } = "";
            [JsonPropertyName("client_event_id")]
            public string ClientEventId { get; set; } = "";
            [JsonPropertyName("outcome")]
            public string Outcome { get; set; } = "";
            [JsonPropertyName("message")]
            public string Message { get; set; } = "";
            [JsonPropertyName("remote_receipt_id")]
            public string RemoteReceiptId { get; set; } = "";
        }

        public sealed class CapabilityPayloadResponse
        {
            [JsonPropertyName("ok")]
            public bool Ok { get; set; }

            [JsonPropertyName("error")]
            public string? Error { get; set; }

            [JsonPropertyName("payload")]
            public CapabilityPayload? Payload { get; set; }
        }

        public sealed class CapabilityPayload
        {
            [JsonPropertyName("employee")]
            public CapabilityEmployee Employee { get; set; } = new CapabilityEmployee();

            [JsonPropertyName("workstation")]
            public CapabilityWorkstation Workstation { get; set; } = new CapabilityWorkstation();

            [JsonPropertyName("modules")]
            public Dictionary<string, bool> Modules { get; set; } = new Dictionary<string, bool>();

            [JsonPropertyName("actions")]
            public Dictionary<string, bool> Actions { get; set; } = new Dictionary<string, bool>();

            [JsonPropertyName("current_job")]
            public CurrentJobContext? CurrentJob { get; set; }

            [JsonPropertyName("recent_job")]
            public CurrentJobContext? RecentJob { get; set; }
        }

        public sealed class CapabilityEmployee
        {
            [JsonPropertyName("remote_employee_id")]
            public string RemoteEmployeeId { get; set; } = "";
            [JsonPropertyName("employee_id")]
            public string EmployeeId { get; set; } = "";

            [JsonPropertyName("employee_code")]
            public string EmployeeCode { get; set; } = "";

            [JsonPropertyName("display_name")]
            public string DisplayName { get; set; } = "";

            [JsonPropertyName("role")]
            public string Role { get; set; } = "";

            [JsonPropertyName("session_timeout_minutes")]
            public int SessionTimeoutMinutes { get; set; } = 15;
        }

        public sealed class CapabilityWorkstation
        {
            [JsonPropertyName("shop_id")]
            public string ShopId { get; set; } = "";

            [JsonPropertyName("shop_name")]
            public string ShopName { get; set; } = "";

            [JsonPropertyName("device_id")]
            public string DeviceId { get; set; } = "";

            [JsonPropertyName("device_name")]
            public string DeviceName { get; set; } = "";
        }

        public sealed class WorkOrderListResponse
        {
            [JsonPropertyName("ok")]
            public bool Ok { get; set; }

            [JsonPropertyName("error")]
            public string? Error { get; set; }

            [JsonPropertyName("work_orders")]
            public List<WorkOrderSummary> WorkOrders { get; set; } = new List<WorkOrderSummary>();

            [JsonPropertyName("current_job")]
            public CurrentJobContext? CurrentJob { get; set; }

            [JsonPropertyName("recent_job")]
            public CurrentJobContext? RecentJob { get; set; }

            [JsonPropertyName("assigned_jobs")]
            public List<WorkOrderSummary> AssignedJobs { get; set; } = new List<WorkOrderSummary>();

            [JsonPropertyName("available_jobs")]
            public List<WorkOrderSummary> AvailableJobs { get; set; } = new List<WorkOrderSummary>();

            [JsonPropertyName("assigned_count")]
            public int AssignedCount { get; set; }

            [JsonPropertyName("backup_count")]
            public int BackupCount { get; set; }

            [JsonPropertyName("shop_awareness")]
            public ShopAwareness? ShopAwareness { get; set; }
        }

        public sealed class ShopAwareness
        {
            [JsonPropertyName("summary")]
            public ShopSummary Summary { get; set; } = new ShopSummary();

            [JsonPropertyName("operators")]
            public List<OperatorAwareness> Operators { get; set; } = new List<OperatorAwareness>();
        }

        public sealed class ShopSummary
        {
            [JsonPropertyName("active_jobs")]
            public int ActiveJobs { get; set; }

            [JsonPropertyName("active_operators")]
            public int ActiveOperators { get; set; }

            [JsonPropertyName("waiting_jobs")]
            public int WaitingJobs { get; set; }

            [JsonPropertyName("completed_jobs")]
            public int CompletedJobs { get; set; }

            [JsonPropertyName("idle_operators")]
            public int IdleOperators { get; set; }

            [JsonPropertyName("visible_jobs")]
            public int VisibleJobs { get; set; }
        }

        public sealed class OperatorAwareness
        {
            [JsonPropertyName("operator_id")]
            public int OperatorId { get; set; }

            [JsonPropertyName("operator_name")]
            public string OperatorName { get; set; } = "";

            [JsonPropertyName("employee_code")]
            public string EmployeeCode { get; set; } = "";

            [JsonPropertyName("status")]
            public string Status { get; set; } = "";

            [JsonPropertyName("work_order_id")]
            public int WorkOrderId { get; set; }

            [JsonPropertyName("work_order_number")]
            public string WorkOrderNumber { get; set; } = "";

            [JsonPropertyName("operation_id")]
            public int OperationId { get; set; }

            [JsonPropertyName("operation_number")]
            public int OperationNumber { get; set; }

            [JsonPropertyName("workstation_name")]
            public string WorkstationName { get; set; } = "";

            [JsonPropertyName("started_utc")]
            public string StartedUtc { get; set; } = "";
        }

        public sealed class WorkOrderSummary
        {
            [JsonPropertyName("work_order_id")]
            public int WorkOrderId { get; set; }

            [JsonPropertyName("work_order_number")]
            public string WorkOrderNumber { get; set; } = "";

            [JsonPropertyName("part_number")]
            public string PartNumber { get; set; } = "";

            [JsonPropertyName("part_description")]
            public string PartDescription { get; set; } = "";

            [JsonPropertyName("revision")]
            public string Revision { get; set; } = "";

            [JsonPropertyName("status")]
            public string Status { get; set; } = "";

            [JsonPropertyName("quantity")]
            public int Quantity { get; set; }

            [JsonPropertyName("due_date")]
            public string DueDate { get; set; } = "";

            [JsonPropertyName("operation_count")]
            public int OperationCount { get; set; }

            [JsonPropertyName("has_drawings")]
            public bool HasDrawings { get; set; }

            [JsonPropertyName("released_utc")]
            public string ReleasedUtc { get; set; } = "";

            [JsonPropertyName("visibility_source")]
            public string VisibilitySource { get; set; } = "";

            [JsonPropertyName("is_current_job")]
            public bool IsCurrentJob { get; set; }

            [JsonPropertyName("is_assigned")]
            public bool IsAssigned { get; set; }

            [JsonPropertyName("is_department_match")]
            public bool IsDepartmentMatch { get; set; }

            [JsonPropertyName("is_backup_job")]
            public bool IsBackupJob { get; set; }

            [JsonPropertyName("active_operation_id")]
            public int ActiveOperationId { get; set; }

            [JsonPropertyName("active_operation_number")]
            public int ActiveOperationNumber { get; set; }

            [JsonPropertyName("active_operation_title")]
            public string ActiveOperationTitle { get; set; } = "";

            [JsonPropertyName("assignment_label")]
            public string AssignmentLabel { get; set; } = "";

            [JsonPropertyName("progress_percent")]
            public int ProgressPercent { get; set; }

            [JsonPropertyName("completed_operations")]
            public int CompletedOperations { get; set; }

            [JsonPropertyName("total_operations")]
            public int TotalOperations { get; set; }

            [JsonPropertyName("operation_summary")]
            public OperationStatusSummary OperationSummary { get; set; } = new OperationStatusSummary();

            [JsonPropertyName("active_operators")]
            public List<ActiveOperator> ActiveOperators { get; set; } = new List<ActiveOperator>();
        }

        public sealed class WorkOrderDetailResponse
        {
            [JsonPropertyName("ok")]
            public bool Ok { get; set; }

            [JsonPropertyName("error")]
            public string? Error { get; set; }

            [JsonPropertyName("message")]
            public string Message { get; set; } = "";

            [JsonPropertyName("work_order")]
            public WorkOrderDetail? WorkOrder { get; set; }
        }

        public sealed class WorkOrderDetail
        {
            [JsonPropertyName("work_order_id")]
            public int WorkOrderId { get; set; }

            [JsonPropertyName("work_order_number")]
            public string WorkOrderNumber { get; set; } = "";

            [JsonPropertyName("part_number")]
            public string PartNumber { get; set; } = "";

            [JsonPropertyName("part_description")]
            public string PartDescription { get; set; } = "";

            [JsonPropertyName("revision")]
            public string Revision { get; set; } = "";

            [JsonPropertyName("status")]
            public string Status { get; set; } = "";

            [JsonPropertyName("quantity")]
            public int Quantity { get; set; }

            [JsonPropertyName("due_date")]
            public string DueDate { get; set; } = "";

            [JsonPropertyName("customer_name")]
            public string CustomerName { get; set; } = "";

            [JsonPropertyName("po_number")]
            public string PoNumber { get; set; } = "";

            [JsonPropertyName("source_component_id")]
            public string SourceComponentId { get; set; } = "";

            [JsonPropertyName("release_state")]
            public string ReleaseState { get; set; } = "";

            [JsonPropertyName("released_utc")]
            public string ReleasedUtc { get; set; } = "";

            [JsonPropertyName("snapshot_load_source")]
            public string SnapshotLoadSource { get; set; } = "";

            [JsonPropertyName("has_drawings")]
            public bool HasDrawings { get; set; }

            [JsonPropertyName("visibility_source")]
            public string VisibilitySource { get; set; } = "";

            [JsonPropertyName("is_current_job")]
            public bool IsCurrentJob { get; set; }

            [JsonPropertyName("is_assigned")]
            public bool IsAssigned { get; set; }

            [JsonPropertyName("assignment_label")]
            public string AssignmentLabel { get; set; } = "";

            [JsonPropertyName("progress_percent")]
            public int ProgressPercent { get; set; }

            [JsonPropertyName("completed_operations")]
            public int CompletedOperations { get; set; }

            [JsonPropertyName("total_operations")]
            public int TotalOperations { get; set; }

            [JsonPropertyName("operation_summary")]
            public OperationStatusSummary OperationSummary { get; set; } = new OperationStatusSummary();

            [JsonPropertyName("active_operators")]
            public List<ActiveOperator> ActiveOperators { get; set; } = new List<ActiveOperator>();

            [JsonPropertyName("operations")]
            public List<WorkOrderOperationSummary> Operations { get; set; } = new List<WorkOrderOperationSummary>();
        }

        public sealed class OperationStatusSummary
        {
            [JsonPropertyName("not_started")]
            public int NotStarted { get; set; }

            [JsonPropertyName("in_progress")]
            public int InProgress { get; set; }

            [JsonPropertyName("completed")]
            public int Completed { get; set; }
        }

        public sealed class ActiveOperator
        {
            [JsonPropertyName("employee_id")]
            public int EmployeeId { get; set; }

            [JsonPropertyName("employee_code")]
            public string EmployeeCode { get; set; } = "";

            [JsonPropertyName("employee_name")]
            public string EmployeeName { get; set; } = "";

            [JsonPropertyName("operation_id")]
            public int OperationId { get; set; }

            [JsonPropertyName("operation_number")]
            public int OperationNumber { get; set; }

            [JsonPropertyName("operation_title")]
            public string OperationTitle { get; set; } = "";

            [JsonPropertyName("workstation_id")]
            public string WorkstationId { get; set; } = "";

            [JsonPropertyName("workstation_name")]
            public string WorkstationName { get; set; } = "";

            [JsonPropertyName("started_utc")]
            public string StartedUtc { get; set; } = "";
        }

        public sealed class CurrentJobContext
        {
            [JsonPropertyName("work_order_id")]
            public int WorkOrderId { get; set; }

            [JsonPropertyName("work_order_number")]
            public string WorkOrderNumber { get; set; } = "";

            [JsonPropertyName("part_number")]
            public string PartNumber { get; set; } = "";

            [JsonPropertyName("part_description")]
            public string PartDescription { get; set; } = "";

            [JsonPropertyName("visibility_source")]
            public string VisibilitySource { get; set; } = "";

            [JsonPropertyName("operation_id")]
            public int OperationId { get; set; }

            [JsonPropertyName("operation_number")]
            public int OperationNumber { get; set; }

            [JsonPropertyName("operation_title")]
            public string OperationTitle { get; set; } = "";

            [JsonPropertyName("operation_status")]
            public string OperationStatus { get; set; } = "";

            [JsonPropertyName("started_utc")]
            public string StartedUtc { get; set; } = "";

            [JsonPropertyName("assignment_label")]
            public string AssignmentLabel { get; set; } = "";
        }

        public sealed class WorkOrderOperationSummary
        {
            [JsonPropertyName("operation_id")]
            public int OperationId { get; set; }

            [JsonPropertyName("operation_number")]
            public int OperationNumber { get; set; }

            [JsonPropertyName("title")]
            public string Title { get; set; } = "";

            [JsonPropertyName("department")]
            public string Department { get; set; } = "";

            [JsonPropertyName("work_center")]
            public string WorkCenter { get; set; } = "";

            [JsonPropertyName("status")]
            public string Status { get; set; } = "";

            [JsonPropertyName("operator_name")]
            public string OperatorName { get; set; } = "";

            [JsonPropertyName("machine_name")]
            public string MachineName { get; set; } = "";

            [JsonPropertyName("started_utc")]
            public string StartedUtc { get; set; } = "";

            [JsonPropertyName("completed_utc")]
            public string CompletedUtc { get; set; } = "";

            [JsonPropertyName("can_start")]
            public bool CanStart { get; set; }

            [JsonPropertyName("can_stop")]
            public bool CanStop { get; set; }

            [JsonPropertyName("can_complete")]
            public bool CanComplete { get; set; }
        }

        public sealed class DrawingPackageResponse
        {
            [JsonPropertyName("ok")]
            public bool Ok { get; set; }

            [JsonPropertyName("error")]
            public string? Error { get; set; }

            [JsonPropertyName("drawing_package")]
            public DrawingPackage? DrawingPackage { get; set; }
        }

        public sealed class DrawingPackage
        {
            [JsonPropertyName("work_order_id")]
            public int WorkOrderId { get; set; }

            [JsonPropertyName("work_order_number")]
            public string WorkOrderNumber { get; set; } = "";

            [JsonPropertyName("part_number")]
            public string PartNumber { get; set; } = "";

            [JsonPropertyName("revision")]
            public string Revision { get; set; } = "";

            [JsonPropertyName("drawings")]
            public List<DrawingReference> Drawings { get; set; } = new List<DrawingReference>();
        }

        public sealed class DrawingReference
        {
            [JsonPropertyName("drawing_id")]
            public string DrawingId { get; set; } = "";

            [JsonPropertyName("label")]
            public string Label { get; set; } = "";

            [JsonPropertyName("revision")]
            public int Revision { get; set; }

            [JsonPropertyName("page_count")]
            public int PageCount { get; set; }

            [JsonPropertyName("is_primary")]
            public bool IsPrimary { get; set; }

            [JsonPropertyName("content_type")]
            public string ContentType { get; set; } = "";

            [JsonPropertyName("download_route")]
            public string DownloadRoute { get; set; } = "";
        }

        public sealed class InspectionTaskResponse
        {
            [JsonPropertyName("ok")]
            public bool Ok { get; set; }

            [JsonPropertyName("error")]
            public string? Error { get; set; }

            [JsonPropertyName("message")]
            public string Message { get; set; } = "";

            [JsonPropertyName("inspection")]
            public InspectionTaskPackage? Inspection { get; set; }
        }

        public sealed class InspectionTaskPackage
        {
            [JsonPropertyName("work_order_id")]
            public int WorkOrderId { get; set; }

            [JsonPropertyName("operation_id")]
            public int OperationId { get; set; }

            [JsonPropertyName("feature_set_id")]
            public int FeatureSetId { get; set; }

            [JsonPropertyName("feature_set_name")]
            public string FeatureSetName { get; set; } = "";

            [JsonPropertyName("template_key")]
            public string TemplateKey { get; set; } = "TemplateA";

            [JsonPropertyName("session_id")]
            public int SessionId { get; set; }

            [JsonPropertyName("tasks")]
            public List<InspectionTask> Tasks { get; set; } = new List<InspectionTask>();
        }

        public sealed class InspectionTask
        {
            [JsonPropertyName("feature_id")]
            public int FeatureId { get; set; }

            [JsonPropertyName("balloon_number")]
            public int BalloonNumber { get; set; }

            [JsonPropertyName("item_number")]
            public string ItemNumber { get; set; } = "";

            [JsonPropertyName("zone")]
            public string Zone { get; set; } = "";

            [JsonPropertyName("feature_text")]
            public string FeatureText { get; set; } = "";

            [JsonPropertyName("nominal")]
            public string Nominal { get; set; } = "";

            [JsonPropertyName("tol_plus")]
            public string TolPlus { get; set; } = "";

            [JsonPropertyName("tol_minus")]
            public string TolMinus { get; set; } = "";

            [JsonPropertyName("units")]
            public string Units { get; set; } = "";

            [JsonPropertyName("classification")]
            public string Classification { get; set; } = "";

            [JsonPropertyName("inspection_method")]
            public string InspectionMethod { get; set; } = "";

            [JsonPropertyName("frequency")]
            public string Frequency { get; set; } = "";

            [JsonPropertyName("input_type")]
            public string InputType { get; set; } = "Text";

            [JsonPropertyName("input_options_json")]
            public string InputOptionsJson { get; set; } = "";

            [JsonPropertyName("notes")]
            public string Notes { get; set; } = "";

            [JsonPropertyName("sample_index")]
            public int SampleIndex { get; set; } = 1;

            [JsonPropertyName("actual_value")]
            public string ActualValue { get; set; } = "";

            [JsonPropertyName("pass_fail")]
            public string PassFail { get; set; } = "";

            [JsonPropertyName("inspector")]
            public string Inspector { get; set; } = "";

            [JsonPropertyName("measured_utc")]
            public string MeasuredUtc { get; set; } = "";

            [JsonPropertyName("result_notes")]
            public string ResultNotes { get; set; } = "";
        }

        public sealed class DrawingContentResponse
        {
            [JsonPropertyName("ok")]
            public bool Ok { get; set; }

            [JsonPropertyName("error")]
            public string? Error { get; set; }

            [JsonPropertyName("file_name")]
            public string FileName { get; set; } = "";

            [JsonPropertyName("content_type")]
            public string ContentType { get; set; } = "";

            [JsonPropertyName("content_base64")]
            public string ContentBase64 { get; set; } = "";

            public byte[] GetBytes()
                => string.IsNullOrWhiteSpace(ContentBase64) ? Array.Empty<byte>() : Convert.FromBase64String(ContentBase64);
        }
    }
}
