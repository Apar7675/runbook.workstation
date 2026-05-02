using RunBook.Workstation.Models;
using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RunBook.Workstation.Services
{
    public static class ControlSessionService
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };
        private static ControlSessionRecord? _sessionOverride;

        public static void ConfigureSessionOverride(ControlSessionRecord? session)
        {
            _sessionOverride = session;
        }

        public static void ClearSessionOverride()
        {
            _sessionOverride = null;
        }

        public static bool HasSession()
        {
            return GetSessionRecord() != null;
        }

        public static string GetStatusLabel()
        {
            var session = GetSessionRecord();
            if (session == null)
                return "Not signed in";

            var label = "Signed in";
            if (!DateTime.TryParse(session.ExpiresAtUtc, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var expiresUtc))
                return label;

            return $"{label} until {expiresUtc.ToLocalTime():g}";
        }

        public static ControlSessionRecord SignIn(string baseUrl, string email, string password)
        {
            var request = new LoginRequest
            {
                Email = (email ?? "").Trim(),
                Password = password ?? ""
            };

            if (string.IsNullOrWhiteSpace(request.Email))
                throw new InvalidOperationException("Control email is required.");

            if (string.IsNullOrWhiteSpace(request.Password))
                throw new InvalidOperationException("Control password is required.");

            var session = PostSession($"{NormalizeBaseUrl(baseUrl)}api/auth/session/login", request);
            WorkstationStorageService.SaveControlSession(session);
            return session;
        }

        public static void SignOut()
        {
            WorkstationStorageService.SaveControlSession(null);
        }

        public static bool CanAuthenticate(WorkstationSettings settings, out string reason)
        {
            try
            {
                _ = GetAccessToken(settings.ControlBaseUrl);
                reason = "";
                return true;
            }
            catch (Exception ex)
            {
                reason = ex.Message;
                return false;
            }
        }

        public static string GetAccessToken(string baseUrl)
        {
            var session = GetSessionRecord() ?? throw new InvalidOperationException("Control supervisor sign-in required.");
            if (NeedsRefresh(session))
            {
                if (string.IsNullOrWhiteSpace(session.RefreshToken))
                {
                    SignOut();
                    throw new InvalidOperationException("Control session expired. Sign in again.");
                }

                try
                {
                    session = RefreshSession(baseUrl, session);
                    WorkstationStorageService.SaveControlSession(session);
                }
                catch
                {
                    SignOut();
                    throw;
                }
            }

            if (string.IsNullOrWhiteSpace(session.AccessToken))
                throw new InvalidOperationException("Control session is missing an access token.");

            return session.AccessToken;
        }

        private static ControlSessionRecord RefreshSession(string baseUrl, ControlSessionRecord current)
        {
            var request = new RefreshRequest
            {
                RefreshToken = current.RefreshToken ?? ""
            };

            return PostSession($"{NormalizeBaseUrl(baseUrl)}api/auth/session/refresh", request);
        }

        private static ControlSessionRecord PostSession(string url, object request)
        {
            using var client = WorkstationHttpClientFactory.Create(timeout: TimeSpan.FromSeconds(20));

            using var content = new StringContent(JsonSerializer.Serialize(request, JsonOptions), Encoding.UTF8, "application/json");
            using var response = client.PostAsync(url, content).GetAwaiter().GetResult();
            var responseText = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            var payload = JsonSerializer.Deserialize<SessionEnvelope>(responseText, JsonOptions)
                ?? new SessionEnvelope { Ok = false, Error = "Control session endpoint returned an invalid response." };

            if (!response.IsSuccessStatusCode || !payload.Ok || payload.Session == null)
                throw new InvalidOperationException(payload.Error ?? $"Control session request failed ({(int)response.StatusCode}).");

            return new ControlSessionRecord
            {
                AccessToken = payload.Session.AccessToken ?? "",
                RefreshToken = payload.Session.RefreshToken ?? "",
                ExpiresAtUtc = payload.Session.ExpiresAtUtc ?? "",
                UserId = payload.Session.UserId ?? "",
                Email = ""
            };
        }

        private static bool NeedsRefresh(ControlSessionRecord session)
        {
            if (!DateTime.TryParse(session.ExpiresAtUtc, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var expiresUtc))
                return true;

            return expiresUtc <= DateTime.UtcNow.AddMinutes(2);
        }

        private static ControlSessionRecord? GetSessionRecord()
        {
            return _sessionOverride ?? WorkstationStorageService.LoadControlSession();
        }

        private static string NormalizeBaseUrl(string baseUrl)
        {
            var normalized = (baseUrl ?? "").Trim();
            if (normalized.Length == 0)
                throw new InvalidOperationException("Control base URL is required.");

            if (!normalized.EndsWith("/", StringComparison.Ordinal))
                normalized += "/";

            return normalized;
        }

        private sealed class LoginRequest
        {
            [JsonPropertyName("email")]
            public string Email { get; set; } = "";

            [JsonPropertyName("password")]
            public string Password { get; set; } = "";
        }

        private sealed class RefreshRequest
        {
            [JsonPropertyName("refresh_token")]
            public string RefreshToken { get; set; } = "";
        }

        private sealed class SessionEnvelope
        {
            [JsonPropertyName("ok")]
            public bool Ok { get; set; }

            [JsonPropertyName("error")]
            public string? Error { get; set; }

            [JsonPropertyName("session")]
            public SessionPayload? Session { get; set; }
        }

        private sealed class SessionPayload
        {
            [JsonPropertyName("access_token")]
            public string? AccessToken { get; set; }

            [JsonPropertyName("refresh_token")]
            public string? RefreshToken { get; set; }

            [JsonPropertyName("expires_at_utc")]
            public string? ExpiresAtUtc { get; set; }

            [JsonPropertyName("user_id")]
            public string? UserId { get; set; }

            [JsonPropertyName("email")]
            public string? Email { get; set; }
        }
    }
}
