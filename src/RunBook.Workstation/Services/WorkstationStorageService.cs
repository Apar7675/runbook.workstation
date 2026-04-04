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

        public static readonly string ProjectRoot = ResolveProjectRoot();
        public static readonly string DataRoot = Path.Combine(ProjectRoot, DataFolderName);
        public static readonly string CoreFolder = Path.Combine(DataRoot, CoreFolderName);
        public static readonly string LogsFolder = Path.Combine(DataRoot, LogsFolderName);
        public static readonly string TempFolder = Path.Combine(DataRoot, TempFolderName);
        public static readonly string CacheFolder = Path.Combine(DataRoot, CacheFolderName);
        public static readonly string ConfigFolder = Path.Combine(DataRoot, ConfigFolderName);
        public static readonly string SettingsPath = Path.Combine(ConfigFolder, "workstation-settings.json");
        public static readonly string SessionPath = Path.Combine(CacheFolder, "workstation-session.dat");
        public static readonly string ControlSessionPath = Path.Combine(CacheFolder, "control-session.dat");
        public static readonly string TimeClockCachePath = Path.Combine(CacheFolder, "timeclock-cache.json");
        public static readonly string RegistrationPath = Path.Combine(CacheFolder, "registration.dat");
        public static readonly string LegacyRegistrationPath = Path.Combine(CacheFolder, "registration.json");
        public static readonly string TimeclockStatePath = Path.Combine(CacheFolder, "timeclock-state.json");
        public static readonly string TimeclockQueuePath = Path.Combine(CacheFolder, "timeclock-queue.json");
        public static readonly string AuthCachePath = Path.Combine(CacheFolder, "employee-auth-cache.dat");
        private static readonly byte[] SessionEntropy = Encoding.UTF8.GetBytes("RunBook.Workstation.Session.v1");
        private static readonly byte[] ControlSessionEntropy = Encoding.UTF8.GetBytes("RunBook.Workstation.ControlSession.v1");
        private static readonly byte[] AuthCacheEntropy = Encoding.UTF8.GetBytes("RunBook.Workstation.EmployeeAuthCache.v1");
        private static readonly byte[] RegistrationEntropy = Encoding.UTF8.GetBytes("RunBook.Workstation.Registration.v2");

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
            Directory.CreateDirectory(ConfigFolder);
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

        public static WorkstationSessionSnapshot? LoadSession() => LoadProtectedJson<WorkstationSessionSnapshot>(SessionPath, SessionEntropy);
        public static void SaveSession(WorkstationSessionSnapshot? session) => SaveProtectedJson(SessionPath, session, SessionEntropy);
        public static ControlSessionRecord? LoadControlSession() => LoadProtectedJson<ControlSessionRecord>(ControlSessionPath, ControlSessionEntropy);
        public static void SaveControlSession(ControlSessionRecord? session) => SaveProtectedJson(ControlSessionPath, session, ControlSessionEntropy);
        public static WorkstationRegistrationSnapshot? LoadRegistration()
        {
            var registration = LoadProtectedJson<WorkstationRegistrationSnapshot>(RegistrationPath, RegistrationEntropy);
            if (registration != null)
                return registration;

            registration = LoadJson<WorkstationRegistrationSnapshot>(LegacyRegistrationPath);
            if (registration != null)
            {
                SaveRegistration(registration);
                if (File.Exists(LegacyRegistrationPath))
                    File.Delete(LegacyRegistrationPath);
            }

            return registration;
        }

        public static void SaveRegistration(WorkstationRegistrationSnapshot? snapshot)
        {
            SaveProtectedJson(RegistrationPath, snapshot, RegistrationEntropy);
            if (File.Exists(LegacyRegistrationPath))
                File.Delete(LegacyRegistrationPath);
        }
        public static WorkstationPunchRecord[] LoadTimeClockCache() => LoadJson<WorkstationPunchRecord[]>(TimeClockCachePath) ?? Array.Empty<WorkstationPunchRecord>();
        public static void SaveTimeClockCache(WorkstationPunchRecord[] punches) => SaveJson(TimeClockCachePath, punches ?? Array.Empty<WorkstationPunchRecord>());
        public static WorkstationTimeclockSnapshot? LoadTimeclockState() => LoadJson<WorkstationTimeclockSnapshot>(TimeclockStatePath);
        public static void SaveTimeclockState(WorkstationTimeclockSnapshot? snapshot) => SaveJson(TimeclockStatePath, snapshot);
        public static WorkstationSyncQueueItem[] LoadTimeclockQueue() => LoadJson<WorkstationSyncQueueItem[]>(TimeclockQueuePath) ?? Array.Empty<WorkstationSyncQueueItem>();
        public static void SaveTimeclockQueue(WorkstationSyncQueueItem[] items) => SaveJson(TimeclockQueuePath, items ?? Array.Empty<WorkstationSyncQueueItem>());
        public static WorkstationEmployeeAuthCache? LoadAuthCache() => LoadProtectedJson<WorkstationEmployeeAuthCache>(AuthCachePath, AuthCacheEntropy);
        public static void SaveAuthCache(WorkstationEmployeeAuthCache? cache) => SaveProtectedJson(AuthCachePath, cache, AuthCacheEntropy);

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
                settings.DesktopBaseUrl = "http://localhost:30111";

            settings.WorkstationName = string.IsNullOrWhiteSpace(settings.WorkstationName)
                ? Environment.MachineName
                : settings.WorkstationName.Trim();

            if (string.IsNullOrWhiteSpace(settings.WorkstationId))
                settings.WorkstationId = NewStableId();
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
