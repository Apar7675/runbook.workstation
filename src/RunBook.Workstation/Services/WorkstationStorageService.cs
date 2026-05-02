using RunBook.Workstation.Models;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RunBook.Workstation.Services
{
    public static class WorkstationStorageService
    {
        private const string DataFolderName = "Data";
        private const string CoreFolderName = "_core";
        private const string LogsFolderName = "_logs";
        private const string TempFolderName = "_temp";
        private const string CacheFolderName = "cache";
        private const string ConfigFolderName = "config";
        private const string AppFolderName = "App";
        private const string MachineFolderName = "_machine";
        private const string WorkstationFolderName = "Workstation";

        public static readonly string ProjectRoot = ResolveProjectRoot();
        public static readonly string DataRoot = Path.Combine(ProjectRoot, DataFolderName);
        public static readonly string CoreFolder = Path.Combine(DataRoot, CoreFolderName);
        public static readonly string LogsFolder = Path.Combine(DataRoot, LogsFolderName);
        public static readonly string TempFolder = Path.Combine(DataRoot, TempFolderName);
        public static readonly string CacheFolder = Path.Combine(DataRoot, CacheFolderName);
        public static readonly string AvatarCacheFolder = Path.Combine(CacheFolder, "avatars");
        public static readonly string ConfigFolder = Path.Combine(DataRoot, ConfigFolderName);
        public static readonly string MachineFolder = Path.Combine(ProjectRoot, AppFolderName, MachineFolderName, WorkstationFolderName);
        public static readonly string SettingsPath = Path.Combine(ConfigFolder, "workstation-settings.json");
        public static readonly string ControlSessionPath = Path.Combine(CacheFolder, "control-session.dat");
        public static readonly string ControlCallDetectionLogPath = Path.Combine(MachineFolder, "control-call-detection.log");
        public static readonly string ControlVerificationResultPath = Path.Combine(MachineFolder, "control-independence-verification.json");
        private static readonly byte[] SessionEntropy = Encoding.UTF8.GetBytes("RunBook.Workstation.Session.v1");
        private static readonly byte[] ControlSessionEntropy = Encoding.UTF8.GetBytes("RunBook.Workstation.ControlSession.v1");
        private static readonly byte[] AuthCacheEntropy = Encoding.UTF8.GetBytes("RunBook.Workstation.EmployeeAuthCache.v1");
        private static readonly byte[] RegistrationEntropy = Encoding.UTF8.GetBytes("RunBook.Workstation.Registration.v2");
        private static readonly byte[] RuntimeAccessTokenEntropy = Encoding.UTF8.GetBytes("RunBook.Workstation.RuntimeAccessToken.v1");

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        static WorkstationStorageService()
        {
            EnsureRuntimeFolders();
        }

        public static void EnsureRuntimeFolders()
        {
            Directory.CreateDirectory(ProjectRoot);
            Directory.CreateDirectory(DataRoot);
            Directory.CreateDirectory(CoreFolder);
            Directory.CreateDirectory(LogsFolder);
            Directory.CreateDirectory(TempFolder);
            Directory.CreateDirectory(CacheFolder);
            Directory.CreateDirectory(AvatarCacheFolder);
            Directory.CreateDirectory(ConfigFolder);
            Directory.CreateDirectory(MachineFolder);
            Directory.CreateDirectory(GetCurrentShopScopedCacheFolder());
        }

        public static WorkstationSettings LoadSettings()
        {
            EnsureRuntimeFolders();
            if (!File.Exists(SettingsPath))
            {
                var settings = CreateDefaultSettings();
                SaveSettings(settings);
                return settings;
            }

            try
            {
                var json = File.ReadAllText(SettingsPath);
                var settings = JsonSerializer.Deserialize<WorkstationSettings>(json, JsonOptions) ?? CreateDefaultSettings();
                EnsureDefaults(settings);
                return settings;
            }
            catch
            {
                return CreateDefaultSettings();
            }
        }

        public static void SaveSettings(WorkstationSettings settings)
        {
            EnsureRuntimeFolders();
            EnsureDefaults(settings);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
        }

        public static WorkstationSessionSnapshot? LoadSession() => LoadProtectedJson<WorkstationSessionSnapshot>(GetSessionPath(), SessionEntropy);
        public static void SaveSession(WorkstationSessionSnapshot? session) => SaveProtectedJson(GetSessionPath(), session, SessionEntropy);
        public static ControlSessionRecord? LoadControlSession() => LoadProtectedJson<ControlSessionRecord>(ControlSessionPath, ControlSessionEntropy);
        public static void SaveControlSession(ControlSessionRecord? session) => SaveProtectedJson(ControlSessionPath, session, ControlSessionEntropy);
        public static WorkstationRegistrationSnapshot? LoadRegistration()
        {
            var registration = LoadProtectedJson<WorkstationRegistrationSnapshot>(GetRegistrationPath(), RegistrationEntropy);
            if (registration != null)
                return registration;

            registration = LoadJson<WorkstationRegistrationSnapshot>(GetLegacyRegistrationPath());
            if (registration != null)
            {
                SaveRegistration(registration);
                DeleteFileIfExists(GetLegacyRegistrationPath());
            }

            return registration;
        }

        public static void SaveRegistration(WorkstationRegistrationSnapshot? snapshot)
        {
            SaveProtectedJson(GetRegistrationPath(), snapshot, RegistrationEntropy);
            DeleteFileIfExists(GetLegacyRegistrationPath());
        }
        public static WorkstationPunchRecord[] LoadTimeClockCache() => LoadJson<WorkstationPunchRecord[]>(GetTimeClockCachePath()) ?? Array.Empty<WorkstationPunchRecord>();
        public static void SaveTimeClockCache(WorkstationPunchRecord[] punches) => SaveJson(GetTimeClockCachePath(), punches ?? Array.Empty<WorkstationPunchRecord>());
        public static WorkstationTimeclockSnapshot? LoadTimeclockState() => LoadJson<WorkstationTimeclockSnapshot>(GetTimeclockStatePath());
        public static void SaveTimeclockState(WorkstationTimeclockSnapshot? snapshot) => SaveJson(GetTimeclockStatePath(), snapshot);
        public static WorkstationSyncQueueItem[] LoadTimeclockQueue() => LoadJson<WorkstationSyncQueueItem[]>(GetTimeclockQueuePath()) ?? Array.Empty<WorkstationSyncQueueItem>();
        public static void SaveTimeclockQueue(WorkstationSyncQueueItem[] items) => SaveJson(GetTimeclockQueuePath(), items ?? Array.Empty<WorkstationSyncQueueItem>());
        public static WorkstationEmployeeAuthCache? LoadAuthCache() => LoadProtectedJson<WorkstationEmployeeAuthCache>(GetAuthCachePath(), AuthCacheEntropy);
        public static void SaveAuthCache(WorkstationEmployeeAuthCache? cache) => SaveProtectedJson(GetAuthCachePath(), cache, AuthCacheEntropy);
        public static WorkstationRuntimeAccessTokenRecord? LoadRuntimeAccessToken() => LoadProtectedJson<WorkstationRuntimeAccessTokenRecord>(GetRuntimeAccessTokenPath(), RuntimeAccessTokenEntropy);
        public static void SaveRuntimeAccessToken(WorkstationRuntimeAccessTokenRecord? token) => SaveProtectedJson(GetRuntimeAccessTokenPath(), token, RuntimeAccessTokenEntropy);

        public static void ClearShopScopedCache(string? shopId, string? safeCompanyName = null)
        {
            var folder = GetShopScopedCacheFolder(shopId, safeCompanyName);
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }

        public static void ClearAllOtherShopScopedCaches(string? activeShopId, string? safeCompanyName = null)
        {
            var shopsRoot = Path.Combine(CacheFolder, "shops");
            if (!Directory.Exists(shopsRoot))
                return;

            var activeKey = BuildShopScopeKey(activeShopId, safeCompanyName);
            foreach (var folder in Directory.GetDirectories(shopsRoot, "*", SearchOption.TopDirectoryOnly))
            {
                var folderName = Path.GetFileName(folder) ?? "";
                if (string.Equals(folderName, activeKey, StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    Directory.Delete(folder, recursive: true);
                }
                catch
                {
                }
            }
        }

        public static string GetCurrentShopScopeKey()
        {
            var settings = LoadSettings();
            return BuildShopScopeKey(settings.ShopId, "");
        }

        public static string NewStableId()
        {
            var bytes = RandomNumberGenerator.GetBytes(16);
            return new Guid(bytes).ToString("D");
        }

        private static T? LoadJson<T>(string path)
        {
            try
            {
                if (!File.Exists(path))
                    return default;

                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<T>(json, JsonOptions);
            }
            catch
            {
                return default;
            }
        }

        private static void SaveJson<T>(string path, T value)
        {
            EnsureRuntimeFolders();
            if (value == null)
            {
                if (File.Exists(path))
                    File.Delete(path);
                return;
            }

            File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions));
        }

        private static T? LoadProtectedJson<T>(string path, byte[] entropy)
        {
            try
            {
                if (!File.Exists(path))
                    return default;

                var cipher = File.ReadAllBytes(path);
                if (cipher.Length == 0)
                    return default;

                var plain = ProtectedData.Unprotect(cipher, entropy, DataProtectionScope.CurrentUser);
                return JsonSerializer.Deserialize<T>(plain, JsonOptions);
            }
            catch
            {
                return default;
            }
        }

        private static void SaveProtectedJson<T>(string path, T value, byte[] entropy)
        {
            EnsureRuntimeFolders();
            if (value == null)
            {
                if (File.Exists(path))
                    File.Delete(path);
                return;
            }

            var json = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
            var cipher = ProtectedData.Protect(json, entropy, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(path, cipher);
        }

        private static WorkstationSettings CreateDefaultSettings()
        {
            var settings = new WorkstationSettings();
            EnsureDefaults(settings);
            return settings;
        }

        private static void EnsureDefaults(WorkstationSettings settings)
        {
            settings.ControlBaseUrl = (settings.ControlBaseUrl ?? "").Trim();
            if (string.IsNullOrWhiteSpace(settings.ControlBaseUrl))
                settings.ControlBaseUrl = "http://localhost:3000";

            settings.DesktopBaseUrl = (settings.DesktopBaseUrl ?? "").Trim();
            if (string.IsNullOrWhiteSpace(settings.DesktopBaseUrl))
                settings.DesktopBaseUrl = "http://localhost:30112";

            settings.WorkstationName = string.IsNullOrWhiteSpace(settings.WorkstationName)
                ? Environment.MachineName
                : settings.WorkstationName.Trim();

            if (string.IsNullOrWhiteSpace(settings.WorkstationId))
                settings.WorkstationId = NewStableId();
        }

        private static string GetCurrentShopScopedCacheFolder()
        {
            var settings = LoadSettingsForScope();
            return GetShopScopedCacheFolder(settings.ShopId, "");
        }

        private static string GetShopScopedCacheFolder(string? shopId, string? safeCompanyName)
        {
            var scopeKey = BuildShopScopeKey(shopId, safeCompanyName);
            var folder = Path.Combine(CacheFolder, "shops", scopeKey);
            Directory.CreateDirectory(folder);
            return folder;
        }

        private static string BuildShopScopeKey(string? shopId, string? safeCompanyName)
        {
            var preferred = !string.IsNullOrWhiteSpace(shopId) ? shopId : safeCompanyName;
            var normalized = (preferred ?? "").Trim();
            if (string.IsNullOrWhiteSpace(normalized))
                normalized = "unassigned";

            var buffer = new StringBuilder();
            foreach (var ch in normalized)
            {
                if (char.IsLetterOrDigit(ch))
                    buffer.Append(char.ToUpperInvariant(ch));
                else if (buffer.Length == 0 || buffer[^1] != '_')
                    buffer.Append('_');
            }

            var key = buffer.ToString().Trim('_');
            return string.IsNullOrWhiteSpace(key) ? "UNASSIGNED" : key;
        }

        private static string GetTimeClockCachePath() => Path.Combine(GetCurrentShopScopedCacheFolder(), "timeclock-cache.json");
        private static string GetSessionPath() => Path.Combine(GetCurrentShopScopedCacheFolder(), "workstation-session.dat");
        private static string GetRegistrationPath() => Path.Combine(GetCurrentShopScopedCacheFolder(), "registration.dat");
        private static string GetLegacyRegistrationPath() => Path.Combine(GetCurrentShopScopedCacheFolder(), "registration.json");
        private static string GetTimeclockStatePath() => Path.Combine(GetCurrentShopScopedCacheFolder(), "timeclock-state.json");
        private static string GetTimeclockQueuePath() => Path.Combine(GetCurrentShopScopedCacheFolder(), "timeclock-queue.json");
        private static string GetAuthCachePath() => Path.Combine(GetCurrentShopScopedCacheFolder(), "employee-auth-cache.dat");
        private static string GetRuntimeAccessTokenPath() => Path.Combine(GetCurrentShopScopedCacheFolder(), "runtime-access-token.dat");

        private static void DeleteFileIfExists(string path)
        {
            if (File.Exists(path))
                File.Delete(path);
        }

        private static WorkstationSettings LoadSettingsForScope()
        {
            try
            {
                if (!File.Exists(SettingsPath))
                    return CreateDefaultSettings();

                var json = File.ReadAllText(SettingsPath);
                var settings = JsonSerializer.Deserialize<WorkstationSettings>(json, JsonOptions) ?? CreateDefaultSettings();
                EnsureDefaults(settings);
                return settings;
            }
            catch
            {
                return CreateDefaultSettings();
            }
        }

        private static string ResolveProjectRoot()
        {
            var baseDir = Path.GetFullPath(AppContext.BaseDirectory);
            var dir = new DirectoryInfo(baseDir);
            for (int i = 0; i < 6 && dir != null; i++)
            {
                if (File.Exists(Path.Combine(dir.FullName, "RunBook.Workstation.csproj")))
                    return dir.FullName;
                dir = dir.Parent;
            }

            var cwd = Directory.GetCurrentDirectory();
            if (File.Exists(Path.Combine(cwd, "RunBook.Workstation.csproj")))
                return cwd;

            return baseDir;
        }
    }
}
