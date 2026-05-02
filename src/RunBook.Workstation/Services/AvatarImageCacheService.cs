using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace RunBook.Workstation.Services
{
    public static class AvatarImageCacheService
    {
        public static async Task<string> ResolveDisplayPathAsync(string avatarDisplayUrl)
        {
            if (string.IsNullOrWhiteSpace(avatarDisplayUrl))
                return "";

            if (avatarDisplayUrl.StartsWith("pack://", StringComparison.OrdinalIgnoreCase))
                return avatarDisplayUrl;

            if (!Uri.TryCreate(avatarDisplayUrl, UriKind.Absolute, out var avatarUri))
                return File.Exists(avatarDisplayUrl) ? avatarDisplayUrl : "";

            if (avatarUri.IsFile)
                return File.Exists(avatarUri.LocalPath) ? avatarUri.LocalPath : "";

            if (avatarUri.Scheme != Uri.UriSchemeHttp && avatarUri.Scheme != Uri.UriSchemeHttps)
                return "";

            WorkstationStorageService.EnsureRuntimeFolders();
            var cacheKey = BuildCacheKey(avatarUri, avatarDisplayUrl);

            var extension = Path.GetExtension(avatarUri.AbsolutePath);
            if (string.IsNullOrWhiteSpace(extension) || extension.Length > 8)
                extension = ".jpg";

            var cacheFilePath = Path.Combine(
                WorkstationStorageService.AvatarCacheFolder,
                ComputeHash(cacheKey) + extension.ToLowerInvariant());

            if (File.Exists(cacheFilePath))
                return cacheFilePath;

            try
            {
                using var client = WorkstationHttpClientFactory.Create(timeout: TimeSpan.FromSeconds(8));
                var bytes = await client.GetByteArrayAsync(avatarUri).ConfigureAwait(false);
                if (bytes.Length == 0)
                    return "";

                await File.WriteAllBytesAsync(cacheFilePath, bytes).ConfigureAwait(false);
                return cacheFilePath;
            }
            catch
            {
                return "";
            }
        }

        private static string ComputeHash(string value)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
            return Convert.ToHexString(bytes);
        }

        private static string BuildCacheKey(Uri avatarUri, string originalValue)
        {
            if (!avatarUri.IsAbsoluteUri)
                return originalValue;

            if (avatarUri.Scheme == Uri.UriSchemeHttp || avatarUri.Scheme == Uri.UriSchemeHttps)
                return avatarUri.GetLeftPart(UriPartial.Path);

            return originalValue;
        }
    }
}
