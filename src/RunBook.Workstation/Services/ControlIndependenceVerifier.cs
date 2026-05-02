using RunBook.Workstation.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RunBook.Workstation.Services
{
    public sealed class ControlIndependenceVerifier
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        private const string DesktopBaseUrl = "http://desktop.local:30111";
        private const string ControlBaseUrl = "http://control.local:3000";
        private const string ShopId = "shop-verify";
        private const string WorkstationId = "ws-verify";
        private const string WorkstationName = "VERIFY-STATION";
        private const int WorkOrderId = 101;
        private const int OperationId = 201;
        private const int FeatureSetId = 301;
        private const int FeatureId = 401;

        public async Task<VerificationResult> RunAsync(CancellationToken cancellationToken = default)
        {
            WorkstationStorageService.EnsureRuntimeFolders();
            ControlCallDetectionService.ConfigureForCurrentProcess(ControlBaseUrl, enabled: true, block: true);
            ControlCallDetectionService.ResetLog();
            ControlCallDetectionService.WriteEvent("verification_start", new
            {
                control_base_url = ControlBaseUrl,
                desktop_base_url = DesktopBaseUrl
            });

            var result = new VerificationResult
            {
                StartedAtUtc = DateTime.UtcNow
            };

            try
            {
                ControlSessionService.ConfigureSessionOverride(new ControlSessionRecord
                {
                    AccessToken = "cached-access",
                    RefreshToken = "cached-refresh",
                    ExpiresAtUtc = DateTime.UtcNow.AddMinutes(30).ToString("O"),
                    UserId = "supervisor-1",
                    Email = "supervisor@example.com"
                });

                var registration = new WorkstationRegistrationSnapshot
                {
                    ShopId = ShopId,
                    WorkstationId = WorkstationId,
                    WorkstationName = WorkstationName,
                    DeviceToken = "device-token",
                    Status = "active",
                    IsActive = true,
                    TokenIssuedUtc = DateTime.UtcNow.ToString("O"),
                    LastSyncUtc = DateTime.UtcNow.ToString("O")
                };

                var settings = new WorkstationSettings
                {
                    ControlBaseUrl = ControlBaseUrl,
                    DesktopBaseUrl = DesktopBaseUrl,
                    ShopId = ShopId,
                    ShopName = "Verification Shop",
                    WorkstationId = WorkstationId,
                    WorkstationName = WorkstationName,
                    PairingCode = "PAIR123"
                };

                var fakeDesktop = new FakeDesktopHttpMessageHandler(settings, registration);
                using var httpClient = WorkstationHttpClientFactory.Create(fakeDesktop, TimeSpan.FromSeconds(5));
                var api = new RunBookWorkstationApiClient(httpClient, () => registration);

                var authPackage = await ExecuteAsync(
                    result,
                    "startup",
                    "Startup bootstrap",
                    () => api.GetLocalAuthPackageAsync(settings, cancellationToken),
                    payload => ((RunBookWorkstationApiClient.LocalAuthPackageResponse)payload).Package?.Employees.Count > 0,
                    payload => new
                    {
                        employees = ((RunBookWorkstationApiClient.LocalAuthPackageResponse)payload).Package?.Employees.Count ?? 0,
                        supervisor_session_present = true
                    }).ConfigureAwait(false);

                RecordWorkflow(
                    result,
                    "employee_roster_load",
                    "Employee roster load",
                    authPackage.Package?.Employees.Count > 0,
                    null,
                    new
                    {
                        employees = authPackage.Package?.Employees.Count ?? 0,
                        source = "Desktop auth package"
                    });

                var login = await ExecuteAsync(
                    result,
                    "employee_login",
                    "Employee workstation sign-in",
                    () => api.LoginLocalEmployeeAsync(settings, "remote-1", "emp-1", "E001", "123456", cancellationToken),
                    payload => !string.IsNullOrWhiteSpace(((RunBookWorkstationApiClient.LocalEmployeeSessionResponse)payload).Session?.Token),
                    payload => new
                    {
                        session_token = ((RunBookWorkstationApiClient.LocalEmployeeSessionResponse)payload).Session?.Token ?? ""
                    }).ConfigureAwait(false);

                var session = BuildSession(login);

                await ExecuteAsync(
                    result,
                    "session_me",
                    "Session capability refresh",
                    () => api.GetSessionMeAsync(settings, session, cancellationToken),
                    payload => ((RunBookWorkstationApiClient.CapabilityPayloadResponse)payload).Payload?.Actions.ContainsKey("workorders.view") == true,
                    payload => new
                    {
                        actions = ((RunBookWorkstationApiClient.CapabilityPayloadResponse)payload).Payload?.Actions.Keys.OrderBy(key => key).ToArray() ?? Array.Empty<string>()
                    }).ConfigureAwait(false);

                await ExecuteJobLoadAsync(result, api, settings, session, cancellationToken).ConfigureAwait(false);
                await ExecuteAsync(
                    result,
                    "operation_start",
                    "Operation start",
                    () => api.StartOperationAsync(settings, session, WorkOrderId, OperationId, "begin", cancellationToken),
                    payload => string.Equals(((RunBookWorkstationApiClient.WorkOrderDetailResponse)payload).WorkOrder?.Operations.FirstOrDefault()?.Status, "in_progress", StringComparison.OrdinalIgnoreCase),
                    payload => new
                    {
                        operation_status = ((RunBookWorkstationApiClient.WorkOrderDetailResponse)payload).WorkOrder?.Operations.FirstOrDefault()?.Status ?? ""
                    }).ConfigureAwait(false);

                await ExecuteInspectionAsync(result, api, settings, session, cancellationToken).ConfigureAwait(false);

                await ExecuteAsync(
                    result,
                    "operation_completion",
                    "Operation completion",
                    () => api.CompleteOperationAsync(settings, session, WorkOrderId, OperationId, "done", cancellationToken),
                    payload => string.Equals(((RunBookWorkstationApiClient.WorkOrderDetailResponse)payload).WorkOrder?.Operations.FirstOrDefault()?.Status, "completed", StringComparison.OrdinalIgnoreCase),
                    payload => new
                    {
                        operation_status = ((RunBookWorkstationApiClient.WorkOrderDetailResponse)payload).WorkOrder?.Operations.FirstOrDefault()?.Status ?? ""
                    }).ConfigureAwait(false);

                result.DesktopRequests = fakeDesktop.RequestLog.ToList();
                result.ControlCallAttempts = CountControlCallAttempts();
                result.Passed = result.ControlCallAttempts == 0 && result.Workflows.All(item => item.Succeeded);
                result.CompletedAtUtc = DateTime.UtcNow;
                ControlCallDetectionService.WriteEvent("verification_complete", new
                {
                    passed = result.Passed,
                    control_call_attempts = result.ControlCallAttempts,
                    workflows = result.Workflows.Count
                });
                SaveResult(result);
                return result;
            }
            catch (Exception ex)
            {
                result.CompletedAtUtc = DateTime.UtcNow;
                result.Passed = false;
                result.FatalError = ex.ToString();
                result.ControlCallAttempts = CountControlCallAttempts();
                SaveResult(result);
                ControlCallDetectionService.WriteEvent("verification_failed", new
                {
                    error = ex.Message
                });
                return result;
            }
            finally
            {
                ControlSessionService.ClearSessionOverride();
                ControlCallDetectionService.ResetForCurrentProcess();
            }
        }

        private static WorkstationSessionSnapshot BuildSession(RunBookWorkstationApiClient.LocalEmployeeSessionResponse login)
        {
            return new WorkstationSessionSnapshot
            {
                Token = login.Session?.Token ?? "",
                IssuedAtUtc = login.Session?.IssuedAtUtc ?? DateTime.UtcNow.ToString("O"),
                ExpiresAtUtc = login.Session?.ExpiresAtUtc ?? DateTime.UtcNow.AddMinutes(15).ToString("O"),
                SessionTimeoutMinutes = login.Session?.SessionTimeoutMinutes ?? 15,
                Employee = new WorkstationEmployeeIdentity
                {
                    RemoteEmployeeId = login.Payload?.Employee.RemoteEmployeeId ?? "remote-1",
                    EmployeeId = login.Payload?.Employee.EmployeeId ?? "emp-1",
                    EmployeeCode = login.Payload?.Employee.EmployeeCode ?? "E001",
                    DisplayName = login.Payload?.Employee.DisplayName ?? "Verifier",
                    Role = login.Payload?.Employee.Role ?? "Operator"
                },
                Actions = new Dictionary<string, bool>(login.Payload?.Actions ?? new Dictionary<string, bool>(), StringComparer.OrdinalIgnoreCase),
                Modules = login.Payload?.Modules.Where(pair => pair.Value).Select(pair => pair.Key).ToList() ?? new List<string>(),
                Capabilities = new WorkstationCapabilityPayload
                {
                    Employee = new WorkstationCapabilityEmployee
                    {
                        RemoteEmployeeId = login.Payload?.Employee.RemoteEmployeeId ?? "remote-1",
                        EmployeeId = login.Payload?.Employee.EmployeeId ?? "emp-1",
                        EmployeeCode = login.Payload?.Employee.EmployeeCode ?? "E001",
                        DisplayName = login.Payload?.Employee.DisplayName ?? "Verifier",
                        Role = login.Payload?.Employee.Role ?? "Operator",
                        SessionTimeoutMinutes = login.Payload?.Employee.SessionTimeoutMinutes ?? 15
                    },
                    Workstation = new WorkstationCapabilityWorkstation
                    {
                        ShopId = login.Payload?.Workstation.ShopId ?? ShopId,
                        ShopName = login.Payload?.Workstation.ShopName ?? "Verification Shop",
                        DeviceId = login.Payload?.Workstation.DeviceId ?? WorkstationId,
                        DeviceName = login.Payload?.Workstation.DeviceName ?? WorkstationName
                    },
                    Modules = new Dictionary<string, bool>(login.Payload?.Modules ?? new Dictionary<string, bool>(), StringComparer.OrdinalIgnoreCase),
                    Actions = new Dictionary<string, bool>(login.Payload?.Actions ?? new Dictionary<string, bool>(), StringComparer.OrdinalIgnoreCase)
                },
                ShopId = login.Payload?.Workstation.ShopId ?? ShopId,
                ShopName = login.Payload?.Workstation.ShopName ?? "Verification Shop",
                WorkstationId = login.Payload?.Workstation.DeviceId ?? WorkstationId,
                WorkstationName = login.Payload?.Workstation.DeviceName ?? WorkstationName
            };
        }

        private static async Task ExecuteJobLoadAsync(VerificationResult result, RunBookWorkstationApiClient api, WorkstationSettings settings, WorkstationSessionSnapshot session, CancellationToken cancellationToken)
        {
            await ExecuteAsync(
                result,
                "job_load",
                "Job load",
                async () =>
                {
                    var workOrders = await api.GetWorkOrdersAsync(settings, session, cancellationToken).ConfigureAwait(false);
                    var detail = await api.GetWorkOrderDetailAsync(settings, session, WorkOrderId, cancellationToken).ConfigureAwait(false);
                    return new JobLoadPayload(workOrders, detail);
                },
                payload => payload.WorkOrders.WorkOrders.Count > 0 && payload.Detail.WorkOrder != null,
                payload => new
                {
                    work_order_count = payload.WorkOrders.WorkOrders.Count,
                    loaded_work_order_id = payload.Detail.WorkOrder?.WorkOrderId ?? 0
                }).ConfigureAwait(false);
        }

        private static async Task ExecuteInspectionAsync(VerificationResult result, RunBookWorkstationApiClient api, WorkstationSettings settings, WorkstationSessionSnapshot session, CancellationToken cancellationToken)
        {
            await ExecuteAsync(
                result,
                "inspection_entry",
                "Inspection entry",
                async () =>
                {
                    var inspection = await api.GetInspectionTasksAsync(settings, session, WorkOrderId, OperationId, cancellationToken).ConfigureAwait(false);
                    var submit = await api.SubmitInspectionResultAsync(settings, session, WorkOrderId, OperationId, FeatureSetId, FeatureId, 1, "1.250", "ok", cancellationToken).ConfigureAwait(false);
                    return new InspectionPayload(inspection, submit);
                },
                payload => payload.Submit.Inspection?.Tasks.FirstOrDefault()?.ActualValue == "1.250",
                payload => new
                {
                    task_count = payload.Inspection.Inspection?.Tasks.Count ?? 0,
                    actual_value = payload.Submit.Inspection?.Tasks.FirstOrDefault()?.ActualValue ?? ""
                }).ConfigureAwait(false);
        }

        private static async Task<T> ExecuteAsync<T>(VerificationResult result, string key, string name, Func<Task<T>> action, Func<T, bool> validator, Func<T, object> detailsFactory)
        {
            try
            {
                var payload = await action().ConfigureAwait(false);
                RecordWorkflow(result, key, name, validator(payload), null, detailsFactory(payload));
                return payload;
            }
            catch (Exception ex)
            {
                RecordWorkflow(result, key, name, false, ex.ToString(), null);
                throw;
            }
        }

        private static void RecordWorkflow(VerificationResult result, string key, string name, bool succeeded, string? error, object? details)
        {
            result.Workflows.Add(new WorkflowResult
            {
                Key = key,
                Name = name,
                Succeeded = succeeded,
                Error = error ?? "",
                Details = details
            });
        }

        private static int CountControlCallAttempts()
        {
            if (!File.Exists(WorkstationStorageService.ControlCallDetectionLogPath))
                return 0;

            return File.ReadLines(WorkstationStorageService.ControlCallDetectionLogPath)
                .Count(line => line.Contains("\"event_name\":\"control_call_attempt\"", StringComparison.Ordinal));
        }

        private static void SaveResult(VerificationResult result)
        {
            WorkstationStorageService.EnsureRuntimeFolders();
            File.WriteAllText(WorkstationStorageService.ControlVerificationResultPath, JsonSerializer.Serialize(result, JsonOptions));
        }

        private sealed record JobLoadPayload(RunBookWorkstationApiClient.WorkOrderListResponse WorkOrders, RunBookWorkstationApiClient.WorkOrderDetailResponse Detail);
        private sealed record InspectionPayload(RunBookWorkstationApiClient.InspectionTaskResponse Inspection, RunBookWorkstationApiClient.InspectionTaskResponse Submit);

        public sealed class VerificationResult
        {
            public bool Passed { get; set; }
            public DateTime StartedAtUtc { get; set; }
            public DateTime CompletedAtUtc { get; set; }
            public int ControlCallAttempts { get; set; }
            public string FatalError { get; set; } = "";
            public List<WorkflowResult> Workflows { get; set; } = new List<WorkflowResult>();
            public List<RequestRecord> DesktopRequests { get; set; } = new List<RequestRecord>();
        }

        public sealed class WorkflowResult
        {
            public string Key { get; set; } = "";
            public string Name { get; set; } = "";
            public bool Succeeded { get; set; }
            public string Error { get; set; } = "";
            public object? Details { get; set; }
        }

        public sealed class RequestRecord
        {
            public string Method { get; set; } = "";
            public string Url { get; set; } = "";
        }

        private sealed class FakeDesktopHttpMessageHandler : HttpMessageHandler
        {
            private readonly WorkstationSettings _settings;
            private readonly WorkstationRegistrationSnapshot _registration;
            private readonly List<RequestRecord> _requestLog = new List<RequestRecord>();

            public FakeDesktopHttpMessageHandler(WorkstationSettings settings, WorkstationRegistrationSnapshot registration)
            {
                _settings = settings;
                _registration = registration;
            }

            public IReadOnlyList<RequestRecord> RequestLog => _requestLog;

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                if (request.RequestUri == null)
                    throw new InvalidOperationException("Request URI is required.");

                _requestLog.Add(new RequestRecord
                {
                    Method = request.Method.Method,
                    Url = request.RequestUri.ToString()
                });

                if (!string.Equals(request.RequestUri.Host, "desktop.local", StringComparison.OrdinalIgnoreCase))
                    return Task.FromResult(Json(HttpStatusCode.ServiceUnavailable, new { ok = false, error = "Unexpected non-Desktop host." }));

                var path = request.RequestUri.AbsolutePath;
                if (path.EndsWith("/api/workstation-local/auth-package", StringComparison.OrdinalIgnoreCase))
                    return Task.FromResult(Json(HttpStatusCode.OK, BuildAuthPackage()));
                if (path.EndsWith("/api/workstation-local/login", StringComparison.OrdinalIgnoreCase))
                    return Task.FromResult(Json(HttpStatusCode.OK, BuildLocalLogin()));
                if (path.EndsWith("/api/workstation-local/session/me", StringComparison.OrdinalIgnoreCase))
                    return Task.FromResult(Json(HttpStatusCode.OK, new { ok = true, payload = BuildCapabilityPayload() }));
                if (path.EndsWith("/api/workstation-local/navigation", StringComparison.OrdinalIgnoreCase))
                    return Task.FromResult(Json(HttpStatusCode.OK, new { ok = true, payload = BuildCapabilityPayload() }));
                if (path.EndsWith("/api/workstation-local/work-orders", StringComparison.OrdinalIgnoreCase) && request.Method == HttpMethod.Get)
                    return Task.FromResult(Json(HttpStatusCode.OK, BuildWorkOrderList()));
                if (path.EndsWith($"/api/workstation-local/work-orders/{WorkOrderId}", StringComparison.OrdinalIgnoreCase))
                    return Task.FromResult(Json(HttpStatusCode.OK, BuildWorkOrderDetail("ready")));
                if (path.EndsWith($"/api/workstation-local/work-orders/{WorkOrderId}/operations/{OperationId}/start", StringComparison.OrdinalIgnoreCase))
                    return Task.FromResult(Json(HttpStatusCode.OK, BuildWorkOrderDetail("in_progress")));
                if (path.EndsWith($"/api/workstation-local/work-orders/{WorkOrderId}/operations/{OperationId}/complete", StringComparison.OrdinalIgnoreCase))
                    return Task.FromResult(Json(HttpStatusCode.OK, BuildWorkOrderDetail("completed")));
                if (path.EndsWith($"/api/workstation-local/work-orders/{WorkOrderId}/inspection-tasks", StringComparison.OrdinalIgnoreCase))
                    return Task.FromResult(Json(HttpStatusCode.OK, BuildInspection("")));
                if (path.EndsWith("/api/workstation-local/inspection-results", StringComparison.OrdinalIgnoreCase))
                    return Task.FromResult(Json(HttpStatusCode.OK, BuildInspection("1.250")));

                return Task.FromResult(Json(HttpStatusCode.NotFound, new { ok = false, error = $"Unhandled fake endpoint: {path}" }));
            }

            private object BuildAuthPackage()
            {
                return new
                {
                    ok = true,
                    package = new
                    {
                        shop_id = _settings.ShopId,
                        workstation_id = _settings.WorkstationId,
                        auth_package_version = 1,
                        package_hash = "pkg-1",
                        last_synced_utc = DateTime.UtcNow.ToString("O"),
                        source_state = "desktop_authoritative",
                        policy = new
                        {
                            warning_after_hours = 24,
                            hard_stop_after_hours = 168
                        },
                        employees = new[]
                        {
                            new
                            {
                                employee_id = "emp-1",
                                remote_employee_id = "remote-1",
                                employee_code = "E001",
                                display_name = "Verifier",
                                role = "Operator",
                                status = "active",
                                is_active = true,
                                workstation_access_enabled = true,
                                can_timeclock = true,
                                can_dashboard_view = true,
                                can_jobs_module = true,
                                can_inspection_entry = true,
                                can_camera_view = false,
                                has_workstation_passcode = true,
                                session_timeout_minutes = 15,
                                avatar_display_url = "",
                                updated_utc = DateTime.UtcNow.ToString("O")
                            }
                        }
                    }
                };
            }

            private object BuildLocalLogin()
            {
                return new
                {
                    ok = true,
                    session = new
                    {
                        token = "session-token",
                        issued_at_utc = DateTime.UtcNow.ToString("O"),
                        expires_at_utc = DateTime.UtcNow.AddMinutes(15).ToString("O"),
                        session_timeout_minutes = 15
                    },
                    payload = BuildCapabilityPayload()
                };
            }

            private object BuildCapabilityPayload()
            {
                return new
                {
                    employee = new
                    {
                        remote_employee_id = "remote-1",
                        employee_id = "emp-1",
                        employee_code = "E001",
                        display_name = "Verifier",
                        role = "Operator",
                        session_timeout_minutes = 15
                    },
                    workstation = new
                    {
                        shop_id = _settings.ShopId,
                        shop_name = _settings.ShopName,
                        device_id = _registration.WorkstationId,
                        device_name = _registration.WorkstationName
                    },
                    modules = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["timeclock"] = true
                    },
                    actions = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["workorders.view"] = true,
                        ["drawings.view"] = true,
                        ["workorders.execute"] = true,
                        ["production.reportQuantity"] = true,
                        ["production.reportScrap"] = true,
                        ["production.addNote"] = true,
                        ["inspection.view"] = true,
                        ["inspection.enterResult"] = true
                    },
                    current_job = new
                    {
                        work_order_id = WorkOrderId,
                        work_order_number = "WO-101",
                        part_number = "PN-101",
                        part_description = "Verification Part",
                        visibility_source = "desktop",
                        operation_id = OperationId,
                        operation_number = 10,
                        operation_title = "Milling",
                        operation_status = "ready",
                        started_utc = "",
                        assignment_label = "Assigned"
                    },
                    recent_job = (object?)null
                };
            }

            private object BuildWorkOrderList()
            {
                return new
                {
                    ok = true,
                    work_orders = new[] { BuildWorkOrderSummary() },
                    current_job = new
                    {
                        work_order_id = WorkOrderId,
                        work_order_number = "WO-101",
                        part_number = "PN-101",
                        part_description = "Verification Part",
                        visibility_source = "desktop",
                        operation_id = OperationId,
                        operation_number = 10,
                        operation_title = "Milling",
                        operation_status = "ready",
                        started_utc = "",
                        assignment_label = "Assigned"
                    },
                    recent_job = (object?)null,
                    assigned_jobs = new[] { BuildWorkOrderSummary() },
                    available_jobs = Array.Empty<object>(),
                    assigned_count = 1,
                    backup_count = 0,
                    shop_awareness = new
                    {
                        summary = new
                        {
                            active_jobs = 1,
                            active_operators = 1,
                            waiting_jobs = 0,
                            completed_jobs = 0,
                            idle_operators = 0,
                            visible_jobs = 1
                        },
                        operators = new[]
                        {
                            new
                            {
                                operator_id = 1,
                                operator_name = "Verifier",
                                employee_code = "E001",
                                status = "active",
                                work_order_id = WorkOrderId,
                                work_order_number = "WO-101",
                                operation_id = OperationId,
                                operation_number = 10,
                                workstation_name = _settings.WorkstationName,
                                started_utc = DateTime.UtcNow.ToString("O")
                            }
                        }
                    }
                };
            }

            private static object BuildWorkOrderSummary()
            {
                return new
                {
                    work_order_id = WorkOrderId,
                    work_order_number = "WO-101",
                    part_number = "PN-101",
                    part_description = "Verification Part",
                    revision = "A",
                    status = "released",
                    quantity = 10,
                    due_date = DateTime.UtcNow.AddDays(1).ToString("yyyy-MM-dd"),
                    operation_count = 1,
                    has_drawings = false,
                    released_utc = DateTime.UtcNow.ToString("O"),
                    visibility_source = "desktop",
                    is_current_job = true,
                    is_assigned = true,
                    is_department_match = true,
                    is_backup_job = false,
                    active_operation_id = OperationId,
                    active_operation_number = 10,
                    active_operation_title = "Milling",
                    assignment_label = "Assigned",
                    progress_percent = 0,
                    completed_operations = 0,
                    total_operations = 1,
                    operation_summary = new
                    {
                        not_started = 1,
                        in_progress = 0,
                        completed = 0
                    },
                    active_operators = Array.Empty<object>()
                };
            }

            private static object BuildWorkOrderDetail(string operationStatus)
            {
                var completed = string.Equals(operationStatus, "completed", StringComparison.OrdinalIgnoreCase);
                var inProgress = string.Equals(operationStatus, "in_progress", StringComparison.OrdinalIgnoreCase);

                return new
                {
                    ok = true,
                    message = "",
                    work_order = new
                    {
                        work_order_id = WorkOrderId,
                        work_order_number = "WO-101",
                        part_number = "PN-101",
                        part_description = "Verification Part",
                        revision = "A",
                        status = "released",
                        quantity = 10,
                        due_date = DateTime.UtcNow.AddDays(1).ToString("yyyy-MM-dd"),
                        customer_name = "Verifier",
                        po_number = "PO-1",
                        source_component_id = "SRC-1",
                        release_state = "released",
                        released_utc = DateTime.UtcNow.ToString("O"),
                        snapshot_load_source = "desktop",
                        has_drawings = false,
                        visibility_source = "desktop",
                        is_current_job = true,
                        is_assigned = true,
                        assignment_label = "Assigned",
                        progress_percent = completed ? 100 : 0,
                        completed_operations = completed ? 1 : 0,
                        total_operations = 1,
                        operation_summary = new
                        {
                            not_started = completed || inProgress ? 0 : 1,
                            in_progress = inProgress ? 1 : 0,
                            completed = completed ? 1 : 0
                        },
                        active_operators = Array.Empty<object>(),
                        operations = new[]
                        {
                            new
                            {
                                operation_id = OperationId,
                                operation_number = 10,
                                title = "Milling",
                                department = "Machining",
                                work_center = "MC-1",
                                status = operationStatus,
                                operator_name = "Verifier",
                                machine_name = "Machine-1",
                                started_utc = inProgress || completed ? DateTime.UtcNow.ToString("O") : "",
                                completed_utc = completed ? DateTime.UtcNow.ToString("O") : "",
                                can_start = !inProgress && !completed,
                                can_stop = inProgress,
                                can_complete = inProgress
                            }
                        }
                    }
                };
            }

            private static object BuildInspection(string actualValue)
            {
                return new
                {
                    ok = true,
                    message = "",
                    inspection = new
                    {
                        work_order_id = WorkOrderId,
                        operation_id = OperationId,
                        feature_set_id = FeatureSetId,
                        feature_set_name = "Critical Features",
                        template_key = "TemplateA",
                        session_id = 1,
                        tasks = new[]
                        {
                            new
                            {
                                feature_id = FeatureId,
                                balloon_number = 1,
                                item_number = "10",
                                zone = "A1",
                                feature_text = "Diameter",
                                nominal = "1.250",
                                tol_plus = "0.005",
                                tol_minus = "-0.005",
                                units = "in",
                                classification = "critical",
                                inspection_method = "caliper",
                                frequency = "first_article",
                                input_type = "Text",
                                input_options_json = "",
                                notes = "",
                                sample_index = 1,
                                actual_value = actualValue,
                                pass_fail = string.IsNullOrWhiteSpace(actualValue) ? "" : "pass",
                                inspector = string.IsNullOrWhiteSpace(actualValue) ? "" : "Verifier",
                                measured_utc = string.IsNullOrWhiteSpace(actualValue) ? "" : DateTime.UtcNow.ToString("O"),
                                result_notes = string.IsNullOrWhiteSpace(actualValue) ? "" : "ok"
                            }
                        }
                    }
                };
            }

            private static HttpResponseMessage Json(HttpStatusCode statusCode, object payload)
            {
                return new HttpResponseMessage(statusCode)
                {
                    Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
                };
            }
        }
    }
}
