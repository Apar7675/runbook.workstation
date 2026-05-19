using RunBook.Workstation.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace RunBook.Workstation.Services
{
    public sealed class RunBookWorkstationApiClient
    {
        private const int LocalServicePort = 30112;
        private const string LocalServiceBaseUrl = "http://localhost:30112";
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly HttpClient _client;
        private readonly Func<WorkstationRegistrationSnapshot?> _registrationProvider;

        public RunBookWorkstationApiClient(HttpClient? client = null, Func<WorkstationRegistrationSnapshot?>? registrationProvider = null)
        {
            _client = client ?? WorkstationHttpClientFactory.Create(timeout: TimeSpan.FromSeconds(20));
            _registrationProvider = registrationProvider ?? WorkstationStorageService.LoadRegistration;
        }

        public async Task<WorkstationRegistrationSnapshot> RegisterAsync(WorkstationSettings settings, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, BuildServiceUrl(settings.DesktopBaseUrl, "api/workstation-local/register"));
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
                TrustStatus = "trusted",
                LastValidatedUtc = DateTime.UtcNow.ToString("O"),
                LastValidationError = "",
                PairingRequiredReason = "",
                TokenExpiresUtc = "",
                RefreshAvailable = false,
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
            var url = BuildServiceUrl(settings.DesktopBaseUrl, $"api/workstation-local/auth-package?shop_id={Uri.EscapeDataString(context.ShopId)}&workstation_id={Uri.EscapeDataString(context.WorkstationId)}");
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            AddLocalDeviceAuthorization(request, settings);
            using var response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var payload = await DeserializeAsync<LocalAuthPackageResponse>(response, cancellationToken).ConfigureAwait(false);
            EnsureSuccess(response, payload?.Error, "Unable to load local employee auth package.");
            return payload ?? throw new InvalidOperationException("Empty auth package response.");
        }

        public async Task<LocalEmployeeSessionResponse> LoginLocalEmployeeAsync(
            WorkstationSettings settings,
            string remoteEmployeeId,
            string employeeId,
            string employeeCode,
            string passcode,
            CancellationToken cancellationToken,
            string? unlockTraceId = null)
        {
            var context = GetLocalDesktopContext(settings);
            using var request = new HttpRequestMessage(HttpMethod.Post, BuildServiceUrl(settings.DesktopBaseUrl, "api/workstation-local/login"));
            AddLocalDeviceAuthorization(request, settings);
            if (!string.IsNullOrWhiteSpace(unlockTraceId))
                request.Headers.TryAddWithoutValidation("X-RunBook-Unlock-Trace-Id", unlockTraceId);
            request.Content = new StringContent(JsonSerializer.Serialize(new
            {
                shop_id = context.ShopId,
                workstation_id = context.WorkstationId,
                remote_employee_id = remoteEmployeeId,
                employee_id = employeeId,
                employee_code = employeeCode,
                passcode
            }, JsonOptions), Encoding.UTF8, "application/json");

            var sendStopwatch = Stopwatch.StartNew();
            DebugLogService.Write($"UnlockTrace | {unlockTraceId ?? "none"} | workstation_local_api_call_start | elapsed_ms=0 | endpoint=/api/workstation-local/login");
            using var response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            DebugLogService.Write($"UnlockTrace | {unlockTraceId ?? "none"} | workstation_local_api_call_end | elapsed_ms={sendStopwatch.ElapsedMilliseconds} | status_code={(int)response.StatusCode}");
            var parseStopwatch = Stopwatch.StartNew();
            var payload = await DeserializeAsync<LocalEmployeeSessionResponse>(response, cancellationToken).ConfigureAwait(false);
            DebugLogService.Write($"UnlockTrace | {unlockTraceId ?? "none"} | response_parsing_complete | elapsed_ms={parseStopwatch.ElapsedMilliseconds}");
            EnsureSuccess(response, payload?.Error, "Unable to sign in to the local workstation authority.");
            if (payload?.Session == null || payload.Payload == null || string.IsNullOrWhiteSpace(payload.Session.Token))
                throw new InvalidOperationException("Local workstation authority did not return a valid workstation sign-in session.");

            return payload;
        }

        public async Task<LocalHealthResponse> GetLocalHealthAsync(WorkstationSettings settings, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, BuildServiceUrl(settings.DesktopBaseUrl, "api/workstation-local/health"));
            using var response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var payload = await DeserializeAsync<LocalHealthResponse>(response, cancellationToken).ConfigureAwait(false);
            EnsureSuccess(response, payload?.Error, "Unable to reach local workstation authority.");
            return payload ?? throw new InvalidOperationException("Empty local workstation authority health response.");
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

        public async Task<DesktopTimeclockStateResponse> GetLocalTimeclockStateAsync(WorkstationSettings settings, WorkstationSessionSnapshot session, CancellationToken cancellationToken)
        {
            var url = BuildServiceSessionUrl(settings, "api/workstation-local/timeclock/state");
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            AddLocalDeviceAuthorization(request, settings);
            AddLocalEmployeeSession(request, session);
            using var response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var payload = await DeserializeAsync<DesktopTimeclockStateResponse>(response, cancellationToken).ConfigureAwait(false);
            EnsureSuccess(response, payload?.Error, "Unable to load local time clock state.");
            return payload ?? throw new InvalidOperationException("Empty local time clock response.");
        }

        public async Task<DesktopSyncResponse> SyncLocalTimeclockAsync(WorkstationSettings settings, WorkstationSessionSnapshot session, IEnumerable<WorkstationSyncQueueItem> items, CancellationToken cancellationToken)
        {
            var body = new DesktopSyncRequest
            {
                ShopId = settings.ShopId,
                WorkstationId = settings.WorkstationId,
                Items = items?.Select(MapSyncItem).ToList() ?? new List<DesktopSyncItem>()
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, BuildServiceSessionUrl(settings, "api/workstation-local/timeclock/sync"));
            AddLocalDeviceAuthorization(request, settings);
            AddLocalEmployeeSession(request, session);
            request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
            using var response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var payload = await DeserializeAsync<DesktopSyncResponse>(response, cancellationToken).ConfigureAwait(false);
            EnsureSuccess(response, payload?.Error, "Unable to sync local time clock items.");
            return payload ?? throw new InvalidOperationException("Empty local time clock sync response.");
        }


        public async Task<CapabilityPayloadResponse> GetSessionMeAsync(WorkstationSettings settings, WorkstationSessionSnapshot session, CancellationToken cancellationToken)
        {
            var url = BuildServiceSessionUrl(settings, "api/workstation-local/session/me");
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
            var url = BuildServiceSessionUrl(settings, "api/workstation-local/navigation");
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            AddLocalDeviceAuthorization(request, settings);
            AddLocalEmployeeSession(request, session);
            using var response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var payload = await DeserializeAsync<CapabilityPayloadResponse>(response, cancellationToken).ConfigureAwait(false);
            EnsureSuccess(response, payload?.Error, "Unable to load workstation navigation.");
            return payload ?? throw new InvalidOperationException("Empty workstation navigation response.");
        }

        public async Task<RuntimeAccessTokenResponse> IssueRuntimeAccessTokenAsync(WorkstationSettings settings, WorkstationRuntimeAccessRequest runtimeAccessRequest, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, BuildLocalSessionUrl(settings, "api/workstation-local/runtime-access-token"));
            AddLocalDeviceAuthorization(request, settings);
            if (!string.IsNullOrWhiteSpace(runtimeAccessRequest.EmployeeSessionToken))
                request.Headers.TryAddWithoutValidation("X-RunBook-Employee-Session", runtimeAccessRequest.EmployeeSessionToken);
            request.Content = new StringContent(JsonSerializer.Serialize(new
            {
                scope_mode = runtimeAccessRequest.ScopeMode,
                shop_id = runtimeAccessRequest.ShopId,
                workstation_id = runtimeAccessRequest.WorkstationId,
                workstation_name = runtimeAccessRequest.WorkstationName,
                operator_id = runtimeAccessRequest.OperatorId,
                operator_display_name = runtimeAccessRequest.OperatorDisplayName
            }, JsonOptions), Encoding.UTF8, "application/json");

            using var response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var payload = await DeserializeAsync<RuntimeAccessTokenResponse>(response, cancellationToken).ConfigureAwait(false);
            EnsureSuccess(response, payload?.Error, "Unable to issue runtime access token.");
            return payload ?? throw new InvalidOperationException("Empty runtime access token response.");
        }

        public async Task<RuntimeAccessTokenResponse> RefreshRuntimeAccessTokenAsync(WorkstationSettings settings, WorkstationRuntimeAccessTokenRecord current, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, BuildLocalSessionUrl(settings, "api/workstation-local/runtime-access-token/refresh"));
            AddLocalDeviceAuthorization(request, settings);
            if (!string.IsNullOrWhiteSpace(current.EmployeeSessionToken))
                request.Headers.TryAddWithoutValidation("X-RunBook-Employee-Session", current.EmployeeSessionToken);
            request.Content = new StringContent(JsonSerializer.Serialize(new
            {
                refresh_token = current.RefreshToken,
                scope_mode = current.ScopeMode,
                shop_id = current.ShopId,
                workstation_id = current.WorkstationId,
                operator_id = current.OperatorId
            }, JsonOptions), Encoding.UTF8, "application/json");

            using var response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var payload = await DeserializeAsync<RuntimeAccessTokenResponse>(response, cancellationToken).ConfigureAwait(false);
            EnsureSuccess(response, payload?.Error, "Unable to refresh runtime access token.");
            return payload ?? throw new InvalidOperationException("Empty runtime token refresh response.");
        }

        public async Task<WorkOrderListResponse> GetWorkOrdersAsync(WorkstationSettings settings, WorkstationSessionSnapshot session, CancellationToken cancellationToken)
        {
            var url = BuildServiceSessionUrl(settings, "api/workstation-local/work-orders");
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
            var url = BuildServiceSessionUrl(settings, $"api/workstation-local/work-orders/{workOrderId}");
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

        public async Task<MaterialReceiptResponse> SaveMaterialReceiptAsync(WorkstationSettings settings, WorkstationSessionSnapshot session, int workOrderId, int operationId, MaterialReceiptSubmitRequest receipt, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, BuildLocalSessionUrl(settings, $"api/workstation-local/work-orders/{workOrderId}/operations/{operationId}/material-receipt"));
            AddLocalDeviceAuthorization(request, settings);
            AddLocalEmployeeSession(request, session);
            request.Content = new StringContent(JsonSerializer.Serialize(receipt, JsonOptions), Encoding.UTF8, "application/json");
            using var response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var payload = await DeserializeAsync<MaterialReceiptResponse>(response, cancellationToken).ConfigureAwait(false);
            EnsureSuccess(response, payload?.Error, "Unable to save material receipt.");
            return payload ?? throw new InvalidOperationException("Empty material receipt response.");
        }

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

        public async Task<MobileCaptureSessionResponse> CreateMobileCaptureSessionAsync(WorkstationSettings settings, WorkstationSessionSnapshot session, int workOrderId, int operationId, string attachmentType, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, BuildLocalSessionUrl(settings, $"api/workstation-local/work-orders/{workOrderId}/operations/{operationId}/mobile-capture/session"));
            AddLocalDeviceAuthorization(request, settings);
            AddLocalEmployeeSession(request, session);
            request.Content = new StringContent(JsonSerializer.Serialize(new
            {
                attachment_type = attachmentType
            }, JsonOptions), Encoding.UTF8, "application/json");
            using var response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var payload = await DeserializeAsync<MobileCaptureSessionResponse>(response, cancellationToken).ConfigureAwait(false);
            EnsureSuccess(response, payload?.Error, "Unable to create mobile capture session.");
            return payload ?? throw new InvalidOperationException("Empty mobile capture session response.");
        }

        public async Task<OperationAttachmentResponse> GetOperationAttachmentsAsync(WorkstationSettings settings, WorkstationSessionSnapshot session, int workOrderId, int operationId, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, BuildLocalSessionUrl(settings, $"api/workstation-local/work-orders/{workOrderId}/operations/{operationId}/attachments"));
            AddLocalDeviceAuthorization(request, settings);
            AddLocalEmployeeSession(request, session);
            using var response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var payload = await DeserializeAsync<OperationAttachmentResponse>(response, cancellationToken).ConfigureAwait(false);
            EnsureSuccess(response, payload?.Error, "Unable to load operation attachments.");
            return payload ?? throw new InvalidOperationException("Empty operation attachment response.");
        }

        public async Task<OperationPacketResponse> GetOperationPacketAsync(WorkstationSettings settings, WorkstationSessionSnapshot session, int workOrderId, int operationId, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, BuildLocalSessionUrl(settings, $"api/workstation-local/work-orders/{workOrderId}/operations/{operationId}/packet"));
            AddLocalDeviceAuthorization(request, settings);
            AddLocalEmployeeSession(request, session);
            using var response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var payload = await DeserializeAsync<OperationPacketResponse>(response, cancellationToken).ConfigureAwait(false);
            EnsureSuccess(response, payload?.Error, "Unable to load operation packet.");
            return payload ?? throw new InvalidOperationException("Empty operation packet response.");
        }

        public async Task<QuantityEventResponse> SubmitQuantityEventAsync(WorkstationSettings settings, WorkstationSessionSnapshot session, int workOrderId, int operationId, QuantityEventSubmitRequest quantityEvent, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, BuildLocalSessionUrl(settings, $"api/workstation-local/work-orders/{workOrderId}/operations/{operationId}/quantity-events"));
            AddLocalDeviceAuthorization(request, settings);
            AddLocalEmployeeSession(request, session);
            request.Content = new StringContent(JsonSerializer.Serialize(quantityEvent, JsonOptions), Encoding.UTF8, "application/json");
            using var response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var payload = await DeserializeAsync<QuantityEventResponse>(response, cancellationToken).ConfigureAwait(false);
            EnsureSuccess(response, payload?.Error, "Unable to save quantity event.");
            return payload ?? throw new InvalidOperationException("Empty quantity event response.");
        }

        public async Task<DrawingContentResponse> DownloadPacketDocumentAsync(WorkstationSettings settings, WorkstationSessionSnapshot session, string relativeRoute, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, BuildLocalSessionUrl(settings, relativeRoute));
            AddLocalDeviceAuthorization(request, settings);
            AddLocalEmployeeSession(request, session);
            using var response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var payload = await DeserializeAsync<DrawingContentResponse>(response, cancellationToken).ConfigureAwait(false);
            EnsureSuccess(response, payload?.Error, "Unable to download packet document.");
            return payload ?? throw new InvalidOperationException("Empty packet document response.");
        }

        public async Task<PacketDocumentThumbnailResponse> DownloadPacketDocumentThumbnailAsync(WorkstationSettings settings, WorkstationSessionSnapshot session, string relativeRoute, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, BuildLocalSessionUrl(settings, relativeRoute));
            AddLocalDeviceAuthorization(request, settings);
            AddLocalEmployeeSession(request, session);
            using var response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            EnsureSuccess(response, null, "Unable to download packet document thumbnail.");
            var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            return new PacketDocumentThumbnailResponse
            {
                ContentType = response.Content.Headers.ContentType?.MediaType ?? "image/png",
                Bytes = bytes
            };
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

        private string BuildLocalSessionUrl(WorkstationSettings settings, string relativeUrl)
        {
            var context = GetLocalDesktopContext(settings);
            var query = $"shop_id={Uri.EscapeDataString(context.ShopId)}&workstation_id={Uri.EscapeDataString(context.WorkstationId)}";
            var separator = relativeUrl.Contains('?') ? "&" : "?";
            return BuildServiceUrl(settings.DesktopBaseUrl, $"{relativeUrl}{separator}{query}");
        }

        private string BuildServiceSessionUrl(WorkstationSettings settings, string relativeUrl)
        {
            var context = GetLocalDesktopContext(settings);
            var query = $"shop_id={Uri.EscapeDataString(context.ShopId)}&workstation_id={Uri.EscapeDataString(context.WorkstationId)}";
            var separator = relativeUrl.Contains('?') ? "&" : "?";
            return BuildServiceUrl(settings.DesktopBaseUrl, $"{relativeUrl}{separator}{query}");
        }

        private void AddLocalDeviceAuthorization(HttpRequestMessage request, WorkstationSettings settings)
        {
            var registration = _registrationProvider();
            if (registration == null || string.IsNullOrWhiteSpace(registration.DeviceToken))
                throw new InvalidOperationException("Workstation registration with RunBook Service is required.");

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", registration.DeviceToken);
            request.Headers.TryAddWithoutValidation("X-RunBook-Workstation-Id", registration.WorkstationId ?? settings.WorkstationId ?? "");
        }

        private (string ShopId, string WorkstationId) GetLocalDesktopContext(WorkstationSettings settings)
        {
            var registration = _registrationProvider();
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

        private static string BuildServiceUrl(string baseUrl, string relativeUrl)
        {
            return BuildUrl(LocalServiceBaseUrl, relativeUrl);
        }

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

            if (response.StatusCode == HttpStatusCode.NotFound)
                throw new InvalidOperationException($"Service endpoint failed: {response.RequestMessage?.RequestUri?.AbsolutePath ?? "(unknown)"} (endpoint not implemented on Service).");

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

        public sealed class LocalHealthResponse
        {
            [JsonPropertyName("ok")]
            public bool Ok { get; set; }

            [JsonPropertyName("error")]
            public string? Error { get; set; }

            [JsonPropertyName("status")]
            public string Status { get; set; } = "";

            [JsonPropertyName("shop_id")]
            public string ShopId { get; set; } = "";

            [JsonPropertyName("company_name")]
            public string CompanyName { get; set; } = "";

            [JsonPropertyName("error_message")]
            public string ErrorMessage { get; set; } = "";

            [JsonPropertyName("missing_folder_name")]
            public string MissingFolderName { get; set; } = "";

            [JsonPropertyName("safe_company_name")]
            public string SafeCompanyName { get; set; } = "";

            [JsonPropertyName("company_data_folder_name")]
            public string CompanyDataFolderName { get; set; } = "";

            [JsonPropertyName("active_company_data_root")]
            public string ActiveCompanyDataRoot { get; set; } = "";

            [JsonPropertyName("context_loaded_utc")]
            public string ContextLoadedUtc { get; set; } = "";

            [JsonPropertyName("context_source")]
            public string ContextSource { get; set; } = "";

            [JsonPropertyName("monitoring_active")]
            public bool MonitoringActive { get; set; }

            [JsonPropertyName("monitored_machine_count")]
            public int MonitoredMachineCount { get; set; }

            [JsonPropertyName("listening_port")]
            public int ListeningPort { get; set; }

            [JsonPropertyName("timestamp_utc")]
            public string TimestampUtc { get; set; } = "";
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

            [JsonPropertyName("employee_avatar_ref")]
            public AvatarRefDto? EmployeeAvatarRef { get; set; }

            [JsonPropertyName("updated_utc")]
            public string UpdatedUtc { get; set; } = "";
        }

        public sealed class AvatarRefDto
        {
            [JsonPropertyName("avatar_asset_id")]
            public string AvatarAssetId { get; set; } = "";

            [JsonPropertyName("employee_public_id")]
            public string EmployeePublicId { get; set; } = "";

            [JsonPropertyName("machine_public_id")]
            public string MachinePublicId { get; set; } = "";

            [JsonPropertyName("local_api_url")]
            public string LocalApiUrl { get; set; } = "";

            [JsonPropertyName("content_hash")]
            public string ContentHash { get; set; } = "";

            [JsonPropertyName("updated_at_utc")]
            public string UpdatedAtUtc { get; set; } = "";

            [JsonPropertyName("content_type")]
            public string ContentType { get; set; } = "";

            [JsonPropertyName("is_placeholder")]
            public bool IsPlaceholder { get; set; } = true;
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

        public sealed class RuntimeAccessTokenResponse
        {
            [JsonPropertyName("ok")]
            public bool Ok { get; set; }

            [JsonPropertyName("error")]
            public string? Error { get; set; }

            [JsonPropertyName("token")]
            public RuntimeAccessTokenPayload? Token { get; set; }
        }

        public sealed class RuntimeAccessTokenPayload
        {
            [JsonPropertyName("access_token")]
            public string AccessToken { get; set; } = "";

            [JsonPropertyName("refresh_token")]
            public string RefreshToken { get; set; } = "";

            [JsonPropertyName("expires_at_utc")]
            public string ExpiresAtUtc { get; set; } = "";

            [JsonPropertyName("refresh_expires_at_utc")]
            public string RefreshExpiresAtUtc { get; set; } = "";

            [JsonPropertyName("token_type")]
            public string TokenType { get; set; } = "Bearer";

            [JsonPropertyName("scope_mode")]
            public string ScopeMode { get; set; } = "workstation";

            [JsonPropertyName("claims")]
            public RuntimeAccessClaims Claims { get; set; } = new RuntimeAccessClaims();
        }

        public sealed class RuntimeAccessClaims
        {
            [JsonPropertyName("iss")]
            public string Issuer { get; set; } = "desktop-local";

            [JsonPropertyName("aud")]
            public string Audience { get; set; } = "runbook-runtime";

            [JsonPropertyName("sub")]
            public string Subject { get; set; } = "";

            [JsonPropertyName("shop_id")]
            public string ShopId { get; set; } = "";

            [JsonPropertyName("workstation_id")]
            public string WorkstationId { get; set; } = "";

            [JsonPropertyName("workstation_name")]
            public string WorkstationName { get; set; } = "";

            [JsonPropertyName("operator_id")]
            public string OperatorId { get; set; } = "";

            [JsonPropertyName("operator_display_name")]
            public string OperatorDisplayName { get; set; } = "";

            [JsonPropertyName("scope_claims")]
            public List<string> ScopeClaims { get; set; } = new List<string>();

            [JsonPropertyName("allowed_channels")]
            public List<string> AllowedChannels { get; set; } = new List<string>();
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

        public sealed class MobileCaptureSessionResponse
        {
            [JsonPropertyName("ok")]
            public bool Ok { get; set; }

            [JsonPropertyName("error")]
            public string? Error { get; set; }

            [JsonPropertyName("capture")]
            public MobileCaptureSession? Capture { get; set; }
        }

        public sealed class MobileCaptureSession
        {
            [JsonPropertyName("token")]
            public string Token { get; set; } = "";

            [JsonPropertyName("capture_url")]
            public string CaptureUrl { get; set; } = "";

            [JsonPropertyName("short_url")]
            public string ShortUrl { get; set; } = "";

            [JsonPropertyName("expires_utc")]
            public string ExpiresUtc { get; set; } = "";

            [JsonPropertyName("attachment_type")]
            public string AttachmentType { get; set; } = "";

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

        public sealed class OperationAttachmentResponse
        {
            [JsonPropertyName("ok")]
            public bool Ok { get; set; }

            [JsonPropertyName("error")]
            public string? Error { get; set; }

            [JsonPropertyName("attachments")]
            public OperationAttachmentPackage? Attachments { get; set; }
        }

        public sealed class OperationAttachmentPackage
        {
            [JsonPropertyName("work_order_id")]
            public int WorkOrderId { get; set; }

            [JsonPropertyName("work_order_number")]
            public string WorkOrderNumber { get; set; } = "";

            [JsonPropertyName("operation_id")]
            public int OperationId { get; set; }

            [JsonPropertyName("operation_number")]
            public int OperationNumber { get; set; }

            [JsonPropertyName("items")]
            public List<OperationAttachment> Items { get; set; } = new List<OperationAttachment>();
        }

        public sealed class OperationAttachment
        {
            [JsonPropertyName("attachment_id")]
            public string AttachmentId { get; set; } = "";

            [JsonPropertyName("file_name")]
            public string FileName { get; set; } = "";

            [JsonPropertyName("attachment_type")]
            public string AttachmentType { get; set; } = "";

            [JsonPropertyName("content_type")]
            public string ContentType { get; set; } = "";

            [JsonPropertyName("size_bytes")]
            public long SizeBytes { get; set; }

            [JsonPropertyName("created_utc")]
            public string CreatedUtc { get; set; } = "";

            [JsonPropertyName("operation_number")]
            public int OperationNumber { get; set; }
        }

        public sealed class OperationPacketResponse
        {
            [JsonPropertyName("ok")]
            public bool Ok { get; set; }

            [JsonPropertyName("error")]
            public string? Error { get; set; }

            [JsonPropertyName("packet")]
            public OperationPacket? Packet { get; set; }
        }

        public sealed class OperationPacket
        {
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

            [JsonPropertyName("operation_type_id")]
            public int OperationTypeId { get; set; }

            [JsonPropertyName("operation_type")]
            public string OperationType { get; set; } = "";

            [JsonPropertyName("operation_type_display")]
            public string OperationTypeDisplay { get; set; } = "";

            [JsonPropertyName("department_display")]
            public string DepartmentDisplay { get; set; } = "";

            [JsonPropertyName("work_center_display")]
            public string WorkCenterDisplay { get; set; } = "";

            [JsonPropertyName("setup_minutes")]
            public double SetupMinutes { get; set; }

            [JsonPropertyName("cycle_minutes")]
            public double CycleMinutes { get; set; }

            [JsonPropertyName("notes_display")]
            public string NotesDisplay { get; set; } = "";

            [JsonPropertyName("detail_notes_display")]
            public string DetailNotesDisplay { get; set; } = "";

            [JsonPropertyName("material_summary_display")]
            public string MaterialSummaryDisplay { get; set; } = "";

            [JsonPropertyName("evidence_required")]
            public int EvidenceRequired { get; set; }

            [JsonPropertyName("require_photo")]
            public bool RequirePhoto { get; set; }

            [JsonPropertyName("require_video")]
            public bool RequireVideo { get; set; }

            [JsonPropertyName("require_qa_signoff")]
            public bool RequireQaSignoff { get; set; }

            [JsonPropertyName("require_supervisor_signoff")]
            public bool RequireSupervisorSignoff { get; set; }

            [JsonPropertyName("require_attachment")]
            public bool RequireAttachment { get; set; }

            [JsonPropertyName("require_checklist")]
            public bool RequireChecklist { get; set; }

            [JsonPropertyName("require_notes")]
            public bool RequireNotes { get; set; }

            [JsonPropertyName("checklist_items")]
            public List<OperationPacketChecklistItem> ChecklistItems { get; set; } = new List<OperationPacketChecklistItem>();

            [JsonPropertyName("reference_items")]
            public List<OperationPacketReferenceItem> ReferenceItems { get; set; } = new List<OperationPacketReferenceItem>();

            [JsonPropertyName("has_preview_routes")]
            public bool HasPreviewRoutes { get; set; }

            [JsonPropertyName("has_thumbnail_routes")]
            public bool HasThumbnailRoutes { get; set; }

            [JsonPropertyName("has_balloon_geometry")]
            public bool HasBalloonGeometry { get; set; }

            [JsonPropertyName("drawing_documents")]
            public List<OperationPacketDocument> DrawingDocuments { get; set; } = new List<OperationPacketDocument>();

            [JsonPropertyName("ballooned_drawing_documents")]
            public List<OperationPacketDocument> BalloonedDrawingDocuments { get; set; } = new List<OperationPacketDocument>();

            [JsonPropertyName("inspection_documents")]
            public List<OperationPacketDocument> InspectionDocuments { get; set; } = new List<OperationPacketDocument>();

            [JsonPropertyName("operation_references")]
            public List<OperationPacketDocument> OperationReferences { get; set; } = new List<OperationPacketDocument>();

            [JsonPropertyName("operation_attachments")]
            public List<OperationAttachment> OperationAttachments { get; set; } = new List<OperationAttachment>();

            [JsonPropertyName("material_documents")]
            public List<OperationPacketDocument> MaterialDocuments { get; set; } = new List<OperationPacketDocument>();

            [JsonPropertyName("material_unit_options")]
            public List<string> MaterialUnitOptions { get; set; } = new List<string>();

            [JsonPropertyName("ordered_quantity")]
            public double? OrderedQuantity { get; set; }

            [JsonPropertyName("completed_quantity")]
            public double CompletedQuantity { get; set; }

            [JsonPropertyName("scrap_quantity")]
            public double ScrapQuantity { get; set; }

            [JsonPropertyName("remaining_quantity")]
            public double? RemainingQuantity { get; set; }

            [JsonPropertyName("accepted_quantity")]
            public double AcceptedQuantity { get; set; }

            [JsonPropertyName("rejected_quantity")]
            public double RejectedQuantity { get; set; }

            [JsonPropertyName("recent_quantity_events")]
            public List<OperationQuantityEvent> RecentQuantityEvents { get; set; } = new List<OperationQuantityEvent>();

            [JsonPropertyName("material_requirements")]
            public List<MaterialRequirementDto> MaterialRequirements { get; set; } = new List<MaterialRequirementDto>();

            [JsonPropertyName("material_receipts")]
            public List<MaterialReceiptDto> MaterialReceipts { get; set; } = new List<MaterialReceiptDto>();

            [JsonPropertyName("material_traces")]
            public List<MaterialTraceDto> MaterialTraces { get; set; } = new List<MaterialTraceDto>();

            [JsonPropertyName("balloon_markers")]
            public List<BalloonMarker> BalloonMarkers { get; set; } = new List<BalloonMarker>();

            [JsonPropertyName("inspection_tasks")]
            public List<InspectionTask> InspectionTasks { get; set; } = new List<InspectionTask>();
        }

        public sealed class OperationQuantityEvent
        {
            [JsonPropertyName("event_id")]
            public string EventId { get; set; } = "";

            [JsonPropertyName("work_order_id")]
            public string WorkOrderId { get; set; } = "";

            [JsonPropertyName("operation_id")]
            public string OperationId { get; set; } = "";

            [JsonPropertyName("operation_number")]
            public int? OperationNumber { get; set; }

            [JsonPropertyName("event_type")]
            public string EventType { get; set; } = "";

            [JsonPropertyName("quantity")]
            public double Quantity { get; set; }

            [JsonPropertyName("good_quantity")]
            public double? GoodQuantity { get; set; }

            [JsonPropertyName("scrap_quantity")]
            public double? ScrapQuantity { get; set; }

            [JsonPropertyName("employee_id")]
            public string EmployeeId { get; set; } = "";

            [JsonPropertyName("employee_name")]
            public string EmployeeName { get; set; } = "";

            [JsonPropertyName("source")]
            public string Source { get; set; } = "";

            [JsonPropertyName("notes")]
            public string Notes { get; set; } = "";

            [JsonPropertyName("created_utc")]
            public string CreatedUtc { get; set; } = "";

            [JsonPropertyName("created_by")]
            public string CreatedBy { get; set; } = "";

            [JsonPropertyName("packing_list_number")]
            public string PackingListNumber { get; set; } = "";

            [JsonPropertyName("operation_display")]
            public string OperationDisplay { get; set; } = "";
        }

        public sealed class QuantityEventSubmitRequest
        {
            [JsonPropertyName("eventType")]
            public string EventType { get; set; } = "OperationCompleted";

            [JsonPropertyName("quantity")]
            public double Quantity { get; set; }

            [JsonPropertyName("goodQuantity")]
            public double? GoodQuantity { get; set; }

            [JsonPropertyName("scrapQuantity")]
            public double? ScrapQuantity { get; set; }

            [JsonPropertyName("notes")]
            public string Notes { get; set; } = "";

            [JsonPropertyName("packingListNumber")]
            public string PackingListNumber { get; set; } = "";

            [JsonPropertyName("employeeId")]
            public string EmployeeId { get; set; } = "";

            [JsonPropertyName("employeeName")]
            public string EmployeeName { get; set; } = "";
        }

        public sealed class QuantityEventResponse
        {
            [JsonPropertyName("ok")]
            public bool Ok { get; set; }

            [JsonPropertyName("error")]
            public string? Error { get; set; }

            [JsonPropertyName("message")]
            public string Message { get; set; } = "";

            [JsonPropertyName("saved_event")]
            public OperationQuantityEvent? SavedEvent { get; set; }

            [JsonPropertyName("work_order_quantity_summary")]
            public QuantitySummary? WorkOrderQuantitySummary { get; set; }

            [JsonPropertyName("operation_quantity_summary")]
            public QuantitySummary? OperationQuantitySummary { get; set; }
        }

        public sealed class QuantitySummary
        {
            [JsonPropertyName("work_order_id")]
            public string WorkOrderId { get; set; } = "";

            [JsonPropertyName("operation_id")]
            public string OperationId { get; set; } = "";

            [JsonPropertyName("operation_number")]
            public int? OperationNumber { get; set; }

            [JsonPropertyName("operation_display")]
            public string OperationDisplay { get; set; } = "";

            [JsonPropertyName("ordered_quantity")]
            public double? OrderedQuantity { get; set; }

            [JsonPropertyName("required_quantity")]
            public double? RequiredQuantity { get; set; }

            [JsonPropertyName("completed_quantity")]
            public double CompletedQuantity { get; set; }

            [JsonPropertyName("accepted_quantity")]
            public double AcceptedQuantity { get; set; }

            [JsonPropertyName("rejected_quantity")]
            public double RejectedQuantity { get; set; }

            [JsonPropertyName("scrap_quantity")]
            public double ScrapQuantity { get; set; }

            [JsonPropertyName("remaining_quantity")]
            public double? RemainingQuantity { get; set; }

            [JsonPropertyName("remaining_to_complete")]
            public double? RemainingToComplete { get; set; }
        }

        public sealed class MaterialRequirementDto
        {
            [JsonPropertyName("material_requirement_id")]
            public int MaterialRequirementId { get; set; }
            [JsonPropertyName("work_order_op_id")]
            public int WorkOrderOpId { get; set; }
            [JsonPropertyName("expected_shape")]
            public string ExpectedShape { get; set; } = "";
            [JsonPropertyName("expected_size")]
            public string ExpectedSize { get; set; } = "";
            [JsonPropertyName("expected_grade")]
            public string ExpectedGrade { get; set; } = "";
            [JsonPropertyName("expected_spec")]
            public string ExpectedSpec { get; set; } = "";
            [JsonPropertyName("expected_quantity")]
            public double ExpectedQuantity { get; set; }
            [JsonPropertyName("expected_unit")]
            public string ExpectedUnit { get; set; } = "";
            [JsonPropertyName("material_summary_display")]
            public string MaterialSummaryDisplay { get; set; } = "";
        }

        public sealed class MaterialReceiptDto
        {
            [JsonPropertyName("material_receipt_id")]
            public int MaterialReceiptId { get; set; }
            [JsonPropertyName("work_order_op_id")]
            public int WorkOrderOpId { get; set; }
            [JsonPropertyName("material_requirement_id")]
            public int MaterialRequirementId { get; set; }
            [JsonPropertyName("received_shape")]
            public string ReceivedShape { get; set; } = "";
            [JsonPropertyName("received_size")]
            public string ReceivedSize { get; set; } = "";
            [JsonPropertyName("received_grade")]
            public string ReceivedGrade { get; set; } = "";
            [JsonPropertyName("received_spec")]
            public string ReceivedSpec { get; set; } = "";
            [JsonPropertyName("received_quantity")]
            public double ReceivedQuantity { get; set; }
            [JsonPropertyName("received_unit")]
            public string ReceivedUnit { get; set; } = "";
            [JsonPropertyName("condition_status")]
            public string ConditionStatus { get; set; } = "";
            [JsonPropertyName("receiver_name")]
            public string ReceiverName { get; set; } = "";
            [JsonPropertyName("received_utc")]
            public string ReceivedUtc { get; set; } = "";
            [JsonPropertyName("notes")]
            public string Notes { get; set; } = "";
        }

        public sealed class MaterialTraceDto
        {
            [JsonPropertyName("material_trace_id")]
            public int MaterialTraceId { get; set; }
            [JsonPropertyName("material_requirement_id")]
            public int MaterialRequirementId { get; set; }
            [JsonPropertyName("material_receipt_id")]
            public int MaterialReceiptId { get; set; }
            [JsonPropertyName("heat_lot_number")]
            public string HeatLotNumber { get; set; } = "";
            [JsonPropertyName("received_quantity")]
            public double ReceivedQuantity { get; set; }
            [JsonPropertyName("unit")]
            public string Unit { get; set; } = "";
            [JsonPropertyName("cert_status")]
            public string CertStatus { get; set; } = "";
            [JsonPropertyName("created_utc")]
            public string CreatedUtc { get; set; } = "";
            [JsonPropertyName("notes")]
            public string Notes { get; set; } = "";
        }

        public sealed class MaterialReceiptSubmitRequest
        {
            [JsonPropertyName("material_requirement_id")]
            public int MaterialRequirementId { get; set; }
            [JsonPropertyName("received_shape")]
            public string ReceivedShape { get; set; } = "";
            [JsonPropertyName("received_size")]
            public string ReceivedSize { get; set; } = "";
            [JsonPropertyName("received_grade")]
            public string ReceivedGrade { get; set; } = "";
            [JsonPropertyName("received_spec")]
            public string ReceivedSpec { get; set; } = "";
            [JsonPropertyName("received_quantity")]
            public double ReceivedQuantity { get; set; }
            [JsonPropertyName("received_unit")]
            public string ReceivedUnit { get; set; } = "";
            [JsonPropertyName("condition_status")]
            public string ConditionStatus { get; set; } = "OK";
            [JsonPropertyName("notes")]
            public string Notes { get; set; } = "";
            [JsonPropertyName("storage_location")]
            public string StorageLocation { get; set; } = "";
            [JsonPropertyName("heat_lots")]
            public List<MaterialReceiptHeatLotSubmitRequest> HeatLots { get; set; } = new List<MaterialReceiptHeatLotSubmitRequest>();
        }

        public sealed class MaterialReceiptHeatLotSubmitRequest
        {
            [JsonPropertyName("heat_lot_number")]
            public string HeatLotNumber { get; set; } = "";
            [JsonPropertyName("quantity")]
            public double Quantity { get; set; }
            [JsonPropertyName("unit")]
            public string Unit { get; set; } = "";
            [JsonPropertyName("cert_received")]
            public bool CertReceived { get; set; }
            [JsonPropertyName("notes")]
            public string Notes { get; set; } = "";
        }

        public sealed class MaterialReceiptResponse
        {
            [JsonPropertyName("ok")]
            public bool Ok { get; set; }
            [JsonPropertyName("error")]
            public string? Error { get; set; }
            [JsonPropertyName("message")]
            public string Message { get; set; } = "";
            [JsonPropertyName("work_order")]
            public WorkOrderDetail? WorkOrder { get; set; }
            [JsonPropertyName("material_receipt_id")]
            public int MaterialReceiptId { get; set; }
            [JsonPropertyName("material_trace_ids")]
            public List<int> MaterialTraceIds { get; set; } = new List<int>();
        }

        public sealed class OperationPacketChecklistItem
        {
            [JsonPropertyName("label")]
            public string Label { get; set; } = "";

            [JsonPropertyName("is_required")]
            public bool IsRequired { get; set; }
        }

        public sealed class OperationPacketReferenceItem
        {
            [JsonPropertyName("display_name")]
            public string DisplayName { get; set; } = "";

            [JsonPropertyName("released_relative_path")]
            public string ReleasedRelativePath { get; set; } = "";

            [JsonPropertyName("category")]
            public string Category { get; set; } = "";
        }

        public sealed class OperationPacketDocument
        {
            [JsonPropertyName("id")]
            public string Id { get; set; } = "";

            [JsonPropertyName("title")]
            public string Title { get; set; } = "";

            [JsonPropertyName("type")]
            public string Type { get; set; } = "";

            [JsonPropertyName("source")]
            public string Source { get; set; } = "";

            [JsonPropertyName("revision")]
            public string Revision { get; set; } = "";

            [JsonPropertyName("page_count")]
            public int PageCount { get; set; }

            [JsonPropertyName("content_type")]
            public string ContentType { get; set; } = "";

            [JsonPropertyName("created_utc")]
            public string CreatedUtc { get; set; } = "";

            [JsonPropertyName("updated_utc")]
            public string UpdatedUtc { get; set; } = "";

            [JsonPropertyName("preview_route")]
            public string PreviewRoute { get; set; } = "";

            [JsonPropertyName("download_route")]
            public string DownloadRoute { get; set; } = "";

            [JsonPropertyName("thumbnail_route")]
            public string ThumbnailRoute { get; set; } = "";

            [JsonPropertyName("operation_number")]
            public int OperationNumber { get; set; }
        }

        public sealed class PacketDocumentThumbnailResponse
        {
            public string ContentType { get; set; } = "image/png";
            public byte[] Bytes { get; set; } = Array.Empty<byte>();
        }

        public sealed class BalloonMarker
        {
            [JsonPropertyName("balloon_number")]
            public int BalloonNumber { get; set; }

            [JsonPropertyName("page")]
            public int Page { get; set; }

            [JsonPropertyName("feature_id")]
            public int FeatureId { get; set; }

            [JsonPropertyName("characteristic_id")]
            public int CharacteristicId { get; set; }

            [JsonPropertyName("has_geometry")]
            public bool HasGeometry { get; set; }

            [JsonPropertyName("x")]
            public double X { get; set; }

            [JsonPropertyName("y")]
            public double Y { get; set; }

            [JsonPropertyName("width")]
            public double Width { get; set; }

            [JsonPropertyName("height")]
            public double Height { get; set; }

            [JsonPropertyName("radius")]
            public double Radius { get; set; }
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

