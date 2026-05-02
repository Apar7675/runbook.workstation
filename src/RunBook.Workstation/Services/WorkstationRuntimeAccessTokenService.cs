using RunBook.Workstation.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RunBook.Workstation.Services
{
    public sealed class WorkstationRuntimeAccessTokenService
    {
        private static readonly TimeSpan RefreshSkew = TimeSpan.FromMinutes(2);
        private readonly RunBookWorkstationApiClient _api;

        public WorkstationRuntimeAccessTokenService(RunBookWorkstationApiClient? api = null)
        {
            _api = api ?? new RunBookWorkstationApiClient();
        }

        public async Task<WorkstationRuntimeAccessTokenRecord> GetOrRefreshAsync(
            WorkstationSettings settings,
            WorkstationRegistrationSnapshot registration,
            WorkstationSessionSnapshot? employeeSession,
            CancellationToken cancellationToken)
        {
            var current = WorkstationStorageService.LoadRuntimeAccessToken();
            var desired = BuildRequest(settings, registration, employeeSession);
            if (CanReuse(current, desired))
                return current!;

            if (CanRefresh(current, desired))
            {
                var refreshed = await _api.RefreshRuntimeAccessTokenAsync(settings, current!, cancellationToken).ConfigureAwait(false);
                var mapped = MapToken(refreshed.Token, desired);
                WorkstationStorageService.SaveRuntimeAccessToken(mapped);
                return mapped;
            }

            var issued = await _api.IssueRuntimeAccessTokenAsync(settings, desired, cancellationToken).ConfigureAwait(false);
            var record = MapToken(issued.Token, desired);
            WorkstationStorageService.SaveRuntimeAccessToken(record);
            return record;
        }

        public void Clear()
        {
            WorkstationStorageService.SaveRuntimeAccessToken(null);
        }

        public static WorkstationRuntimeAccessRequest BuildRequest(
            WorkstationSettings settings,
            WorkstationRegistrationSnapshot registration,
            WorkstationSessionSnapshot? employeeSession)
        {
            var employee = employeeSession?.Employee;
            var hasOperator = employeeSession != null
                && !string.IsNullOrWhiteSpace(employeeSession.Token)
                && !string.IsNullOrWhiteSpace(employee?.DisplayName);

            return new WorkstationRuntimeAccessRequest
            {
                ScopeMode = hasOperator ? "operator" : "workstation",
                ShopId = registration.ShopId ?? settings.ShopId ?? "",
                WorkstationId = registration.WorkstationId ?? settings.WorkstationId ?? "",
                WorkstationName = registration.WorkstationName ?? settings.WorkstationName ?? "",
                OperatorId = hasOperator
                    ? (!string.IsNullOrWhiteSpace(employee?.RemoteEmployeeId)
                        ? employee!.RemoteEmployeeId
                        : employee?.EmployeeId ?? "")
                    : "",
                OperatorDisplayName = hasOperator ? employee?.DisplayName ?? "" : "",
                EmployeeSessionToken = hasOperator ? employeeSession!.Token : ""
            };
        }

        public static IReadOnlyList<string> BuildRecommendedClaims(WorkstationRuntimeAccessRequest request)
        {
            var claims = new List<string>
            {
                "runtime.messaging.connect",
                "runtime.messaging.read"
            };

            if (string.Equals(request.ScopeMode, "operator", StringComparison.OrdinalIgnoreCase))
                claims.Add("runtime.messaging.publish.operator");
            else
                claims.Add("runtime.messaging.publish.workstation");

            return claims;
        }

        public static IReadOnlyList<string> BuildRecommendedChannels(WorkstationRuntimeAccessRequest request)
        {
            var channels = new List<string>
            {
                $"shops:{request.ShopId}",
                $"shops:{request.ShopId}:workstations:{request.WorkstationId}"
            };

            if (!string.IsNullOrWhiteSpace(request.OperatorId))
                channels.Add($"shops:{request.ShopId}:operators:{request.OperatorId}");

            return channels;
        }

        private static bool CanReuse(WorkstationRuntimeAccessTokenRecord? current, WorkstationRuntimeAccessRequest desired)
        {
            if (current == null || string.IsNullOrWhiteSpace(current.AccessToken))
                return false;

            if (!ScopeMatches(current, desired))
                return false;

            return DateTime.TryParse(current.ExpiresAtUtc, null, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var expiresUtc)
                && expiresUtc > DateTime.UtcNow.Add(RefreshSkew);
        }

        private static bool CanRefresh(WorkstationRuntimeAccessTokenRecord? current, WorkstationRuntimeAccessRequest desired)
        {
            if (current == null || string.IsNullOrWhiteSpace(current.RefreshToken))
                return false;

            if (!ScopeMatches(current, desired))
                return false;

            return DateTime.TryParse(current.RefreshExpiresAtUtc, null, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var refreshExpiresUtc)
                && refreshExpiresUtc > DateTime.UtcNow.AddMinutes(1);
        }

        private static bool ScopeMatches(WorkstationRuntimeAccessTokenRecord current, WorkstationRuntimeAccessRequest desired)
        {
            return string.Equals(current.ScopeMode, desired.ScopeMode, StringComparison.OrdinalIgnoreCase)
                && string.Equals(current.ShopId, desired.ShopId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(current.WorkstationId, desired.WorkstationId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(current.OperatorId, desired.OperatorId, StringComparison.OrdinalIgnoreCase);
        }

        private static WorkstationRuntimeAccessTokenRecord MapToken(RunBookWorkstationApiClient.RuntimeAccessTokenPayload? payload, WorkstationRuntimeAccessRequest desired)
        {
            if (payload == null || string.IsNullOrWhiteSpace(payload.AccessToken))
                throw new InvalidOperationException("Runtime access token is not available in this build.");

            var claims = payload.Claims ?? new RunBookWorkstationApiClient.RuntimeAccessClaims();
            return new WorkstationRuntimeAccessTokenRecord
            {
                AccessToken = payload.AccessToken,
                RefreshToken = payload.RefreshToken,
                ExpiresAtUtc = payload.ExpiresAtUtc,
                RefreshExpiresAtUtc = payload.RefreshExpiresAtUtc,
                TokenType = payload.TokenType,
                ScopeMode = string.IsNullOrWhiteSpace(payload.ScopeMode) ? desired.ScopeMode : payload.ScopeMode,
                ShopId = string.IsNullOrWhiteSpace(claims.ShopId) ? desired.ShopId : claims.ShopId,
                WorkstationId = string.IsNullOrWhiteSpace(claims.WorkstationId) ? desired.WorkstationId : claims.WorkstationId,
                WorkstationName = string.IsNullOrWhiteSpace(claims.WorkstationName) ? desired.WorkstationName : claims.WorkstationName,
                OperatorId = string.IsNullOrWhiteSpace(claims.OperatorId) ? desired.OperatorId : claims.OperatorId,
                OperatorDisplayName = string.IsNullOrWhiteSpace(claims.OperatorDisplayName) ? desired.OperatorDisplayName : claims.OperatorDisplayName,
                EmployeeSessionToken = desired.EmployeeSessionToken,
                IssuedAtUtc = DateTime.UtcNow.ToString("O"),
                ScopeClaims = claims.ScopeClaims?.Any() == true ? claims.ScopeClaims : BuildRecommendedClaims(desired).ToList(),
                AllowedChannels = claims.AllowedChannels?.Any() == true ? claims.AllowedChannels : BuildRecommendedChannels(desired).ToList(),
                Issuer = claims.Issuer,
                Audience = claims.Audience
            };
        }
    }
}
