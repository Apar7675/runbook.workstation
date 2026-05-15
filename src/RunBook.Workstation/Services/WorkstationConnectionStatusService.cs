using RunBook.Workstation.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RunBook.Workstation.Services
{
    public sealed class WorkstationConnectionStatusService
    {
        private const int LocalServicePort = 30112;
        private const string LocalServiceBaseUrl = "http://localhost:30112";
        private static readonly TimeSpan LocalHostHealthyWindow = TimeSpan.FromSeconds(6);
        private static readonly TimeSpan LocalHostRecentlyReachableWindow = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan LocalHostFailureGraceWindow = TimeSpan.FromSeconds(8);
        private static readonly TimeSpan LocalHostProbeTimeout = TimeSpan.FromMilliseconds(3000);
        private static readonly TimeSpan DesktopHealthyWindow = TimeSpan.FromSeconds(6);
        private static readonly TimeSpan DesktopRecentlyReachableWindow = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan DesktopProbeTimeout = TimeSpan.FromMilliseconds(1200);
        private static readonly TimeSpan DesktopProbePerAddressTimeout = TimeSpan.FromMilliseconds(300);
        private static readonly TimeSpan SessionWarningWindow = TimeSpan.FromMinutes(5);

        public IReadOnlyList<ConnectionStatusItem> BuildStatuses(
            WorkstationSettings settings,
            WorkstationRegistrationSnapshot? registration,
            WorkstationSessionSnapshot? employeeSession,
            DateTime? lastLocalHostProbeCheckedUtc,
            DateTime? lastLocalHostSuccessUtc,
            DateTime? lastLocalHostFailureUtc,
            string? lastLocalHostFailureReason,
            DateTime? lastDesktopProbeCheckedUtc,
            DateTime? lastDesktopProbeSuccessUtc,
            DateTime? lastDesktopProbeFailureUtc,
            string? lastDesktopProbeFailureReason)
        {
            return new[]
            {
                BuildLocalHostStatus(settings, registration, lastLocalHostProbeCheckedUtc, lastLocalHostSuccessUtc, lastLocalHostFailureUtc, lastLocalHostFailureReason),
                BuildDesktopLinkStatus(settings, registration, lastDesktopProbeCheckedUtc, lastDesktopProbeSuccessUtc, lastDesktopProbeFailureUtc, lastDesktopProbeFailureReason),
                BuildControlStatus(settings),
                BuildSupabaseStatus(settings),
                BuildEmployeeSessionStatus(employeeSession),
                BuildTrustStatus(registration),
            };
        }

        public async Task<LocalHostProbeResult> ProbeLocalHostAsync(WorkstationSettings settings, CancellationToken cancellationToken = default)
        {
            var checkedUtc = DateTime.UtcNow;
            try
            {
                var builder = new UriBuilder(LocalServiceBaseUrl)
                {
                    Path = "/api/workstation-local/health"
                };

                using var request = new HttpRequestMessage(HttpMethod.Get, builder.Uri);
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(LocalHostProbeTimeout);
                using var response = await WorkstationHttpClientFactory
                    .GetSharedLocalServiceProbeClient()
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token)
                    .ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                    return LocalHostProbeResult.Success(checkedUtc);

                return LocalHostProbeResult.Failed($"RunBook.Service health check returned {(int)response.StatusCode} {response.ReasonPhrase}.", checkedUtc);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return LocalHostProbeResult.Failed($"RunBook.Service health check timed out after {(int)LocalHostProbeTimeout.TotalMilliseconds} ms. (localhost:{LocalServicePort})", checkedUtc);
            }
            catch (Exception ex)
            {
                return LocalHostProbeResult.Failed($"{ex.Message} (localhost:{LocalServicePort})", checkedUtc);
            }
        }

        public DesktopProbeResult ProbeDesktop(WorkstationSettings settings, WorkstationRegistrationSnapshot? registration)
        {
            if (string.IsNullOrWhiteSpace(settings.DesktopBaseUrl))
            {
                return DesktopProbeResult.Skipped("Desktop URL is not configured.");
            }

            if (registration == null || !registration.IsActive || string.IsNullOrWhiteSpace(registration.DeviceToken))
            {
                return DesktopProbeResult.Skipped("Workstation is not enrolled with Desktop.");
            }

            if (!Uri.TryCreate(settings.DesktopBaseUrl, UriKind.Absolute, out var desktopUri))
            {
                return DesktopProbeResult.Failed("Desktop URL is invalid.");
            }

            var port = desktopUri.IsDefaultPort ? (string.Equals(desktopUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ? 443 : 80) : desktopUri.Port;
            var checkedUtc = DateTime.UtcNow;

            try
            {
                var addresses = Dns.GetHostAddresses(desktopUri.Host)
                    .OrderBy(address => address.AddressFamily == AddressFamily.InterNetwork ? 0 : 1)
                    .ToArray();

                if (addresses.Length == 0)
                {
                    return DesktopProbeResult.Failed($"Live Desktop probe could not resolve any addresses. ({desktopUri.Host}:{port})", checkedUtc);
                }

                var timeoutAtUtc = checkedUtc + DesktopProbeTimeout;
                string? lastFailure = null;

                foreach (var address in addresses)
                {
                    var remaining = timeoutAtUtc - DateTime.UtcNow;
                    if (remaining <= TimeSpan.Zero)
                        break;

                    var attemptTimeout = remaining < DesktopProbePerAddressTimeout ? remaining : DesktopProbePerAddressTimeout;
                    if (TryConnect(address, port, attemptTimeout, out var attemptFailure))
                        return DesktopProbeResult.Success(checkedUtc);

                    lastFailure = attemptFailure;
                }

                var message = string.IsNullOrWhiteSpace(lastFailure)
                    ? $"Live Desktop probe timed out after {(int)DesktopProbeTimeout.TotalMilliseconds} ms. ({desktopUri.Host}:{port})"
                    : $"{lastFailure} ({desktopUri.Host}:{port})";
                return DesktopProbeResult.Failed(message, checkedUtc);
            }
            catch (Exception ex)
            {
                return DesktopProbeResult.Failed($"{ex.Message} ({desktopUri.Host}:{port})", checkedUtc);
            }
        }

        private static bool TryConnect(IPAddress address, int port, TimeSpan timeout, out string failureReason)
        {
            try
            {
                using var tcpClient = new TcpClient(address.AddressFamily);
                var connectTask = tcpClient.ConnectAsync(address, port);
                if (!connectTask.Wait(timeout))
                {
                    failureReason = $"Live Desktop probe timed out after {(int)Math.Max(1, Math.Round(timeout.TotalMilliseconds))} ms for {address}:{port}";
                    return false;
                }

                if (connectTask.IsFaulted)
                {
                    failureReason = connectTask.Exception?.GetBaseException().Message ?? $"Live Desktop probe failed for {address}:{port}";
                    return false;
                }

                failureReason = "";
                return true;
            }
            catch (Exception ex)
            {
                failureReason = ex.Message;
                return false;
            }
        }

        private static ConnectionStatusItem BuildControlStatus(WorkstationSettings settings)
        {
            var item = new ConnectionStatusItem
            {
                Key = "control_link",
                Label = "Supervisor",
                LastCheckedUtc = DateTime.Now.ToString("g", CultureInfo.CurrentCulture)
            };

            if (string.IsNullOrWhiteSpace(settings.ControlBaseUrl))
            {
                item.Health = ConnectionHealth.Disconnected;
                item.Reason = "Supervisor sign-in is unavailable because the Control URL is not configured.";
                return item;
            }

            item.Health = ControlSessionService.HasSession()
                ? ConnectionHealth.Healthy
                : ConnectionHealth.Disconnected;
            item.Reason = ControlSessionService.HasSession()
                ? "Supervisor sign-in is active for explicit admin and Control actions."
                : "No active supervisor sign-in. This only affects explicit admin and Control actions.";
            return item;
        }

        private static ConnectionStatusItem BuildSupabaseStatus(WorkstationSettings settings)
        {
            var item = new ConnectionStatusItem
            {
                Key = "supabase_link",
                Label = "Supabase",
                LastCheckedUtc = DateTime.Now.ToString("g", CultureInfo.CurrentCulture)
            };

            if (string.IsNullOrWhiteSpace(settings.ControlBaseUrl))
            {
                item.Health = ConnectionHealth.Disconnected;
                item.Reason = "Shared runtime services are not configured in this workstation build.";
                return item;
            }

            item.Health = ConnectionHealth.Healthy;
            item.Reason = "Supabase-backed runtime services are configured separately from supervisor sign-in. Runtime messaging and realtime features must use scoped workstation or operator access, not supervisor auth.";
            return item;
        }

        private static ConnectionStatusItem BuildDesktopLinkStatus(
            WorkstationSettings settings,
            WorkstationRegistrationSnapshot? registration,
            DateTime? lastDesktopProbeCheckedUtc,
            DateTime? lastDesktopProbeSuccessUtc,
            DateTime? lastDesktopProbeFailureUtc,
            string? lastDesktopProbeFailureReason)
        {
            var nowUtc = DateTime.UtcNow;
            var item = new ConnectionStatusItem
            {
                Key = "desktop_link",
                Label = "Desktop (Legacy)",
                LastCheckedUtc = ToLocalTimestamp(lastDesktopProbeCheckedUtc ?? lastDesktopProbeSuccessUtc ?? lastDesktopProbeFailureUtc)
            };

            if (string.IsNullOrWhiteSpace(settings.DesktopBaseUrl))
            {
                item.Health = ConnectionHealth.Disconnected;
                item.Reason = "Desktop URL is not configured. This only affects deferred legacy endpoints.";
                return item;
            }

            if (registration == null || !registration.IsActive || string.IsNullOrWhiteSpace(registration.DeviceToken))
            {
                item.Health = ConnectionHealth.Disconnected;
                item.Reason = "Legacy Desktop link is not enrolled. Core local access now runs through RunBook.Service.";
                return item;
            }

            if (lastDesktopProbeSuccessUtc.HasValue)
            {
                var age = nowUtc - lastDesktopProbeSuccessUtc.Value;
                if (age <= DesktopHealthyWindow)
                {
                    item.Health = ConnectionHealth.Healthy;
                    item.Reason = "Desktop is reachable. This only matters for deferred workstation endpoints that still remain on Desktop.";
                    return item;
                }
            }

            if (lastDesktopProbeFailureUtc.HasValue)
            {
                var failureAge = nowUtc - lastDesktopProbeFailureUtc.Value;
                if (lastDesktopProbeSuccessUtc.HasValue)
                {
                    var successAge = nowUtc - lastDesktopProbeSuccessUtc.Value;
                    if (successAge <= DesktopRecentlyReachableWindow)
                    {
                        item.Health = ConnectionHealth.Degraded;
                        item.Reason = $"Desktop was reachable {ToAge(successAge)} ago, but the latest legacy probe failed {ToAge(failureAge)} ago.";
                        return item;
                    }
                }

                item.Health = ConnectionHealth.Disconnected;
                item.Reason = string.IsNullOrWhiteSpace(lastDesktopProbeFailureReason)
                    ? "Desktop is unreachable. Core local access flows now use RunBook.Service; deferred legacy endpoints may fail."
                    : lastDesktopProbeFailureReason!;
                return item;
            }

            if (lastDesktopProbeSuccessUtc.HasValue)
            {
                var age = nowUtc - lastDesktopProbeSuccessUtc.Value;
                if (age <= DesktopRecentlyReachableWindow)
                {
                    item.Health = ConnectionHealth.Degraded;
                    item.Reason = $"Desktop was reachable recently, but the last legacy probe was {ToAge(age)} ago.";
                    return item;
                }

                item.Health = ConnectionHealth.Disconnected;
                item.Reason = $"Desktop was reachable before, but no legacy probe has succeeded for {ToAge(age)}.";
                return item;
            }

            item.Health = ConnectionHealth.Degraded;
            item.Reason = "Waiting for the first legacy Desktop probe. Core local access does not depend on Desktop.";
            return item;
        }

        private static ConnectionStatusItem BuildLocalHostStatus(
            WorkstationSettings settings,
            WorkstationRegistrationSnapshot? registration,
            DateTime? lastLocalHostProbeCheckedUtc,
            DateTime? lastLocalHostSuccessUtc,
            DateTime? lastLocalHostFailureUtc,
            string? lastLocalHostFailureReason)
        {
            var nowUtc = DateTime.UtcNow;
            var needsWorkstationTrust = registration == null || !registration.IsActive || string.IsNullOrWhiteSpace(registration.DeviceToken);
            var item = new ConnectionStatusItem
            {
                Key = "local_host",
                Label = "Service",
                LastCheckedUtc = ToLocalTimestamp(lastLocalHostProbeCheckedUtc ?? lastLocalHostSuccessUtc ?? lastLocalHostFailureUtc)
            };

            if (needsWorkstationTrust)
            {
                item.Health = ConnectionHealth.Degraded;
                item.Reason = "RunBook.Service health can still be checked, but workstation trust is not fully enrolled yet.";
            }

            if (lastLocalHostSuccessUtc.HasValue)
            {
                var age = nowUtc - lastLocalHostSuccessUtc.Value;
                if (age <= LocalHostHealthyWindow)
                {
                    item.Health = ConnectionHealth.Healthy;
                    item.Reason = needsWorkstationTrust
                        ? "RunBook.Service is reachable. Employee auth still needs workstation registration/trust before operator tiles can load."
                        : "RunBook.Service is reachable and is the primary local host for workstation access.";
                    return item;
                }
            }

            if (lastLocalHostFailureUtc.HasValue && (!lastLocalHostSuccessUtc.HasValue || lastLocalHostFailureUtc.Value > lastLocalHostSuccessUtc.Value))
            {
                var failureAge = nowUtc - lastLocalHostFailureUtc.Value;
                if (lastLocalHostSuccessUtc.HasValue)
                {
                    var successAge = nowUtc - lastLocalHostSuccessUtc.Value;
                    if (successAge <= LocalHostRecentlyReachableWindow)
                    {
                        if (failureAge <= LocalHostFailureGraceWindow)
                        {
                            item.Health = ConnectionHealth.Healthy;
                            item.Reason = $"RunBook.Service was confirmed healthy {ToAge(successAge)} ago. Waiting to see whether the latest missed probe {ToAge(failureAge)} ago was transient.";
                            return item;
                        }

                        item.Health = ConnectionHealth.Degraded;
                        item.Reason = $"RunBook.Service was reachable {ToAge(successAge)} ago, but the latest local host probe failed {ToAge(failureAge)} ago.";
                        return item;
                    }
                }

                item.Health = ConnectionHealth.Disconnected;
                item.Reason = string.IsNullOrWhiteSpace(lastLocalHostFailureReason)
                    ? "RunBook.Service is unreachable right now."
                    : lastLocalHostFailureReason!;
                return item;
            }

            if (lastLocalHostSuccessUtc.HasValue)
            {
                var age = nowUtc - lastLocalHostSuccessUtc.Value;
                if (age <= LocalHostRecentlyReachableWindow)
                {
                    item.Health = ConnectionHealth.Degraded;
                    item.Reason = $"RunBook.Service was reachable recently, but the last fresh probe was {ToAge(age)} ago.";
                    return item;
                }

                item.Health = ConnectionHealth.Disconnected;
                item.Reason = $"RunBook.Service was reachable before, but no fresh probe has succeeded for {ToAge(age)}.";
                return item;
            }

            if (item.Reason.Length == 0)
            {
                item.Health = ConnectionHealth.Degraded;
                item.Reason = "Waiting for the first RunBook.Service local host probe.";
            }

            return item;
        }

        public readonly struct DesktopProbeResult
        {
            private DesktopProbeResult(bool attempted, bool succeeded, DateTime checkedUtc, string? failureReason)
            {
                Attempted = attempted;
                Succeeded = succeeded;
                CheckedUtc = checkedUtc;
                FailureReason = failureReason ?? "";
            }

            public bool Attempted { get; }
            public bool Succeeded { get; }
            public DateTime CheckedUtc { get; }
            public string FailureReason { get; }

            public static DesktopProbeResult Skipped(string? failureReason)
                => new DesktopProbeResult(false, false, DateTime.UtcNow, failureReason);

            public static DesktopProbeResult Success(DateTime checkedUtc)
                => new DesktopProbeResult(true, true, checkedUtc, "");

            public static DesktopProbeResult Failed(string? failureReason, DateTime? checkedUtc = null)
                => new DesktopProbeResult(true, false, checkedUtc ?? DateTime.UtcNow, failureReason);
        }

        public readonly struct LocalHostProbeResult
        {
            private LocalHostProbeResult(bool attempted, bool succeeded, DateTime checkedUtc, string? failureReason)
            {
                Attempted = attempted;
                Succeeded = succeeded;
                CheckedUtc = checkedUtc;
                FailureReason = failureReason ?? "";
            }

            public bool Attempted { get; }
            public bool Succeeded { get; }
            public DateTime CheckedUtc { get; }
            public string FailureReason { get; }

            public static LocalHostProbeResult Skipped(string? failureReason)
                => new LocalHostProbeResult(false, false, DateTime.UtcNow, failureReason);

            public static LocalHostProbeResult Success(DateTime checkedUtc)
                => new LocalHostProbeResult(true, true, checkedUtc, "");

            public static LocalHostProbeResult Failed(string? failureReason, DateTime? checkedUtc = null)
                => new LocalHostProbeResult(true, false, checkedUtc ?? DateTime.UtcNow, failureReason);
        }

        private static ConnectionStatusItem BuildEmployeeSessionStatus(WorkstationSessionSnapshot? employeeSession)
        {
            var item = new ConnectionStatusItem
            {
                Key = "employee_session",
                Label = "Session",
                LastCheckedUtc = DateTime.Now.ToString("g", CultureInfo.CurrentCulture)
            };

            if (employeeSession == null || string.IsNullOrWhiteSpace(employeeSession.Token))
            {
                item.Health = ConnectionHealth.Disconnected;
                item.Reason = "No active employee workstation session.";
                return item;
            }

            if (!DateTime.TryParse(employeeSession.ExpiresAtUtc, null, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var expiresUtc))
            {
                item.Health = ConnectionHealth.Degraded;
                item.Reason = "Employee session expiry is unavailable.";
                return item;
            }

            var remaining = expiresUtc - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                item.Health = ConnectionHealth.Disconnected;
                item.Reason = "Employee session expired. Sign in again.";
                return item;
            }

            if (remaining <= SessionWarningWindow)
            {
                item.Health = ConnectionHealth.Degraded;
                item.Reason = $"Employee session expires in {ToAge(remaining)}.";
                return item;
            }

            item.Health = ConnectionHealth.Healthy;
            item.Reason = "Employee workstation session is valid.";
            return item;
        }

        private static ConnectionStatusItem BuildTrustStatus(WorkstationRegistrationSnapshot? registration)
        {
            var item = new ConnectionStatusItem
            {
                Key = "workstation_trust",
                Label = "Trust",
                LastCheckedUtc = ToLocalTimestamp(ParseUtc(registration?.LastSyncUtc))
            };

            if (registration == null)
            {
                item.Health = ConnectionHealth.Disconnected;
                item.Reason = "Workstation is not enrolled.";
                return item;
            }

            if (!registration.IsActive || !string.Equals(registration.Status, "active", StringComparison.OrdinalIgnoreCase))
            {
                item.Health = ConnectionHealth.Disconnected;
                item.Reason = "Local workstation registration is no longer valid.";
                return item;
            }

            if (string.Equals(registration.TrustStatus, "degraded", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(registration.TrustStatus, "offline", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(registration.TrustStatus, "blocked", StringComparison.OrdinalIgnoreCase))
            {
                item.Health = ConnectionHealth.Degraded;
                item.Reason = string.IsNullOrWhiteSpace(registration.LastValidationError)
                    ? "Saved workstation pairing is being used while Service trust validation is unavailable."
                    : registration.LastValidationError;
                return item;
            }

            item.Health = ConnectionHealth.Healthy;
            item.Reason = string.IsNullOrWhiteSpace(registration.TrustStatus)
                ? "Trusted workstation registration is valid."
                : $"Trusted workstation registration is {registration.TrustStatus}.";
            return item;
        }

        private static string ToAge(TimeSpan age)
        {
            var positive = age.Duration();
            if (positive.TotalMinutes < 1)
                return $"{Math.Max(1, (int)Math.Round(positive.TotalSeconds))}s";
            if (positive.TotalHours < 1)
                return $"{Math.Max(1, (int)Math.Round(positive.TotalMinutes))}m";
            if (positive.TotalDays < 1)
                return $"{Math.Max(1, (int)Math.Round(positive.TotalHours))}h";

            return $"{Math.Max(1, (int)Math.Round(positive.TotalDays))}d";
        }

        private static DateTime? ParseUtc(string? value)
        {
            if (!DateTime.TryParse(value, null, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
                return null;

            return parsed;
        }

        private static string ToLocalTimestamp(DateTime? value)
            => value.HasValue ? value.Value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) : "";
    }
}



