using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace RunBook.Workstation.Services
{
    public static class ControlCallDetectionService
    {
        private const string EnableEnvVar = "RUNBOOK_WORKSTATION_CONTROL_GUARD";
        private const string BlockEnvVar = "RUNBOOK_WORKSTATION_BLOCK_CONTROL";
        private static readonly object Gate = new object();
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = false
        };

        private static string? _overrideControlBaseUrl;
        private static bool? _overrideEnabled;
        private static bool? _overrideBlocking;

        public static bool IsEnabled
            => _overrideEnabled ?? IsTruthy(Environment.GetEnvironmentVariable(EnableEnvVar));

        public static bool IsBlockingEnabled
            => _overrideBlocking ?? IsTruthy(Environment.GetEnvironmentVariable(BlockEnvVar));

        public static void ConfigureForCurrentProcess(string? controlBaseUrl, bool enabled, bool block)
        {
            _overrideControlBaseUrl = controlBaseUrl;
            _overrideEnabled = enabled;
            _overrideBlocking = block;
        }

        public static void ResetForCurrentProcess()
        {
            _overrideControlBaseUrl = null;
            _overrideEnabled = null;
            _overrideBlocking = null;
        }

        public static void ResetLog()
        {
            WorkstationStorageService.EnsureRuntimeFolders();
            lock (Gate)
            {
                if (File.Exists(WorkstationStorageService.ControlCallDetectionLogPath))
                    File.Delete(WorkstationStorageService.ControlCallDetectionLogPath);
            }
        }

        public static void WriteEvent(string eventName, object details)
        {
            if (!IsEnabled)
                return;

            AppendLog(new
            {
                timestamp_utc = DateTime.UtcNow.ToString("O"),
                event_name = eventName,
                details
            });
        }

        public static void InspectRequest(HttpRequestMessage request)
        {
            if (!IsEnabled || request.RequestUri == null)
                return;

            var controlBaseUrl = ResolveControlBaseUrl();
            if (string.IsNullOrWhiteSpace(controlBaseUrl))
                return;

            if (!Uri.TryCreate(controlBaseUrl, UriKind.Absolute, out var controlUri))
                return;

            if (!IsControlRequest(request.RequestUri, controlUri))
                return;

            var stackTrace = new StackTrace(2, true).ToString();
            AppendLog(new
            {
                timestamp_utc = DateTime.UtcNow.ToString("O"),
                severity = "ERROR",
                event_name = "control_call_attempt",
                request_method = request.Method.Method,
                request_url = request.RequestUri.ToString(),
                control_base_url = controlUri.ToString(),
                blocking_enabled = IsBlockingEnabled,
                stack_trace = stackTrace
            });

            if (IsBlockingEnabled)
                throw new InvalidOperationException($"CONTROL_CALL_BLOCKED: {request.Method.Method} {request.RequestUri}");
        }

        public static HttpMessageHandler CreateGuardHandler(HttpMessageHandler innerHandler)
            => new ControlCallGuardHandler(innerHandler);

        private static string? ResolveControlBaseUrl()
        {
            if (!string.IsNullOrWhiteSpace(_overrideControlBaseUrl))
                return _overrideControlBaseUrl;

            try
            {
                return WorkstationStorageService.LoadSettings().ControlBaseUrl;
            }
            catch
            {
                return null;
            }
        }

        private static bool IsControlRequest(Uri requestUri, Uri controlUri)
        {
            var left = NormalizeBaseUri(requestUri);
            var right = NormalizeBaseUri(controlUri);
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeBaseUri(Uri uri)
        {
            var builder = new UriBuilder(uri)
            {
                Path = string.Empty,
                Query = string.Empty,
                Fragment = string.Empty
            };

            return builder.Uri.ToString().TrimEnd('/');
        }

        private static void AppendLog(object entry)
        {
            try
            {
                WorkstationStorageService.EnsureRuntimeFolders();
                var line = JsonSerializer.Serialize(entry, JsonOptions);
                lock (Gate)
                {
                    File.AppendAllText(WorkstationStorageService.ControlCallDetectionLogPath, line + Environment.NewLine);
                }
            }
            catch
            {
            }
        }

        private static bool IsTruthy(string? value)
            => !string.IsNullOrWhiteSpace(value)
               && (string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase));

        private sealed class ControlCallGuardHandler : DelegatingHandler
        {
            public ControlCallGuardHandler(HttpMessageHandler innerHandler)
                : base(innerHandler)
            {
            }

            protected override System.Threading.Tasks.Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
            {
                InspectRequest(request);
                return base.SendAsync(request, cancellationToken);
            }
        }
    }
}
