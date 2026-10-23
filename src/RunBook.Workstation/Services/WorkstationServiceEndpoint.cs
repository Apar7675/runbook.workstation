using RunBook.Workstation.Models;
using System;
using System.Globalization;
using System.Net;

namespace RunBook.Workstation.Services
{
    internal static class WorkstationServiceEndpoint
    {
        public const string DefaultUrl = "http://localhost:30112";

        public static Uri Resolve(WorkstationSettings settings)
        {
            var forced = Environment.GetEnvironmentVariable("RUNBOOK_WORKSTATION_FORCE_LOCAL_SERVICE");
            if (forced == "1" || string.Equals(forced, "true", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(forced, "yes", StringComparison.OrdinalIgnoreCase))
                return new Uri(DefaultUrl);
            return Parse(settings.DesktopBaseUrl, settings.AllowLanService);
        }

        public static Uri Parse(string? value, bool allowLan)
        {
            var text = string.IsNullOrWhiteSpace(value) ? DefaultUrl : value;
            if (text != text.Trim() || text.IndexOfAny(new[] { '\\', '?', '#', '\r', '\n', '\t' }) >= 0 ||
                !Uri.TryCreate(text, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
                !string.IsNullOrEmpty(uri.UserInfo))
                throw new ArgumentException("Enter a Service URL without credentials, query, fragment or path.");

            var marker = text.IndexOf("://", StringComparison.Ordinal);
            var authorityAndPath = text.Substring(marker + 3);
            var slash = authorityAndPath.IndexOf('/');
            var authority = slash >= 0 ? authorityAndPath.Substring(0, slash) : authorityAndPath;
            var path = slash >= 0 ? authorityAndPath.Substring(slash) : "";
            if (path != "" && path != "/")
                throw new ArgumentException("The Service URL must not contain a path or traversal.");
            var colon = authority.LastIndexOf(':');
            if (colon <= 0 || !int.TryParse(authority.Substring(colon + 1), NumberStyles.None,
                CultureInfo.InvariantCulture, out var port) || port < 1 || port > 65535 || uri.Port != port)
                throw new ArgumentException("The Service URL must include an explicit valid port.");

            var rawHost = authority.Substring(0, colon).Trim('[', ']');
            var host = uri.Host.Trim('[', ']');
            if (!string.Equals(rawHost, host, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Use the canonical Service host address.");
            var loopback = host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                host == "127.0.0.1" || host == "::1";
            if (!loopback)
            {
                if (!allowLan || !IPAddress.TryParse(host, out var address) ||
                    !IsPrivateAddress(address) || !string.Equals(address.ToString(), host, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("LAN Service access requires explicit approval and a private IP address.");
            }
            var formatted = host.Contains(':') ? "[" + host.ToLowerInvariant() + "]" : host.ToLowerInvariant();
            return new Uri(uri.Scheme + "://" + formatted + ":" + port.ToString(CultureInfo.InvariantCulture));
        }

        private static bool IsPrivateAddress(IPAddress address)
        {
            var bytes = address.GetAddressBytes();
            if (bytes.Length == 4)
                return bytes[0] == 10 || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
                       (bytes[0] == 192 && bytes[1] == 168);
            return bytes.Length == 16 && (bytes[0] & 0xfe) == 0xfc;
        }

        public static string Canonical(Uri endpoint)
        {
            var host = endpoint.Host.Trim('[', ']');
            if (host.Contains(':')) host = "[" + host + "]";
            return endpoint.Scheme + "://" + host.ToLowerInvariant() + ":" + endpoint.Port.ToString(CultureInfo.InvariantCulture) + "/";
        }

        public static bool RegistrationMatches(WorkstationRegistrationSnapshot registration, Uri endpoint)
        {
            var recorded = string.IsNullOrWhiteSpace(registration.ServiceEndpoint)
                ? new Uri(DefaultUrl)
                : Parse(registration.ServiceEndpoint, allowLan: true);
            return string.Equals(recorded.AbsoluteUri, endpoint.AbsoluteUri, StringComparison.OrdinalIgnoreCase);
        }

        public static string BuildUrl(Uri endpoint, string relativeRoute)
        {
            var route = (relativeRoute ?? "").TrimStart('/');
            if (!route.StartsWith("api/workstation-local/", StringComparison.Ordinal) ||
                route.IndexOfAny(new[] { '\\', '#', '\r', '\n' }) >= 0)
                throw new ArgumentException("The Service route is invalid.");
            var query = route.IndexOf('?');
            var path = query >= 0 ? route.Substring(0, query) : route;
            foreach (var segment in path.Split('/'))
            {
                var decoded = Uri.UnescapeDataString(segment);
                if (decoded == "." || decoded == ".." || decoded.IndexOfAny(new[] { '/', '\\' }) >= 0)
                    throw new ArgumentException("The Service route must not contain traversal.");
            }
            return endpoint.AbsoluteUri.TrimEnd('/') + "/" + route;
        }
    }
}
