using RunBook.Workstation.Models;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace RunBook.Workstation.Services
{
    public sealed class WorkstationConnectionStatusService
    {
        private static readonly TimeSpan DesktopHealthyWindow = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan DesktopStaleWindow = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan SessionWarningWindow = TimeSpan.FromMinutes(5);

        public IReadOnlyList<ConnectionStatusItem> BuildStatuses(
            WorkstationSettings settings,
            WorkstationRegistrationSnapshot? registration,
            WorkstationSessionSnapshot? employeeSession,
            DateTime? lastDesktopSuccessUtc,
            DateTime? lastDesktopFailureUtc,
            string? lastDesktopFailureReason)
        {
            return new[]
            {
                BuildDesktopLinkStatus(settings, registration, lastDesktopSuccessUtc, lastDesktopFailureUtc, lastDesktopFailureReason),
                BuildEmployeeSessionStatus(employeeSession),
                BuildTrustStatus(registration),
            };
        }

        private static ConnectionStatusItem BuildDesktopLinkStatus(
            WorkstationSettings settings,
            WorkstationRegistrationSnapshot? registration,
            DateTime? lastDesktopSuccessUtc,
            DateTime? lastDesktopFailureUtc,
            string? lastDesktopFailureReason)
        {
            var nowUtc = DateTime.UtcNow;
            var item = new ConnectionStatusItem
            {
                Key = "desktop_link",
                Label = "Desktop",
                LastCheckedUtc = ToLocalTimestamp(lastDesktopSuccessUtc ?? lastDesktopFailureUtc)
            };

            if (string.IsNullOrWhiteSpace(settings.DesktopBaseUrl))
            {
                item.Health = ConnectionHealth.Disconnected;
                item.Reason = "Desktop URL is not configured.";
                return item;
            }

            if (registration == null || !registration.IsActive || string.IsNullOrWhiteSpace(registration.DeviceToken))
            {
                item.Health = ConnectionHealth.Disconnected;
                item.Reason = "Workstation is not enrolled with Desktop.";
                return item;
            }

            if (lastDesktopSuccessUtc.HasValue)
            {
                var age = nowUtc - lastDesktopSuccessUtc.Value;
                if (age <= DesktopHealthyWindow)
                {
                    item.Health = ConnectionHealth.Healthy;
                    item.Reason = "Desktop is reachable and responding.";
                    return item;
                }

                if (age <= DesktopStaleWindow)
                {
                    item.Health = ConnectionHealth.Degraded;
                    item.Reason = $"Desktop check is stale ({ToAge(age)} old).";
                    return item;
                }

                item.Health = ConnectionHealth.Degraded;
                item.Reason = $"Desktop was reachable, but the last good check was {ToAge(age)} ago.";
                return item;
            }

            if (lastDesktopFailureUtc.HasValue)
            {
                item.Health = ConnectionHealth.Disconnected;
                item.Reason = string.IsNullOrWhiteSpace(lastDesktopFailureReason)
                    ? "Desktop is unreachable."
                    : lastDesktopFailureReason!;
                return item;
            }

            item.Health = ConnectionHealth.Degraded;
            item.Reason = "Waiting for the first Desktop health check.";
            return item;
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
                item.Reason = "Desktop no longer trusts this workstation.";
                return item;
            }

            item.Health = ConnectionHealth.Healthy;
            item.Reason = "Trusted workstation registration is valid.";
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
