using RunBook.Workstation.Models;
using System;
using System.Globalization;

namespace RunBook.Workstation.Services
{
    public static class WorkstationEmployeeAuthService
    {
        public static WorkstationAuthCacheHealth EvaluateCache(WorkstationEmployeeAuthCache? cache, DateTime utcNow)
        {
            var health = new WorkstationAuthCacheHealth();
            if (cache == null || cache.Package == null || cache.Package.Employees.Count == 0)
            {
                health.State = WorkstationAuthCacheState.Missing;
                health.Message = "No Desktop-issued employee auth cache is available yet.";
                return health;
            }

            if (!DateTime.TryParse(cache.LastRefreshSuccessUtc, null, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var successUtc))
                successUtc = DateTime.MinValue;

            if (successUtc == DateTime.MinValue)
            {
                health.State = WorkstationAuthCacheState.Missing;
                health.Message = "Employee auth cache exists but has never completed a successful Desktop sync.";
                return health;
            }

            var age = utcNow - successUtc;
            health.Age = age < TimeSpan.Zero ? TimeSpan.Zero : age;
            health.LastSuccessUtc = successUtc.ToString("O");

            var warningHours = Math.Max(1, cache.Package.Policy.WarningAfterHours);
            var hardStopHours = Math.Max(warningHours, cache.Package.Policy.HardStopAfterHours);
            if (health.Age >= TimeSpan.FromHours(hardStopHours))
            {
                health.State = WorkstationAuthCacheState.Expired;
                health.Message = $"Cached employee auth expired after {hardStopHours} hours without Desktop refresh.";
                return health;
            }

            if (health.Age >= TimeSpan.FromHours(warningHours))
            {
                health.State = WorkstationAuthCacheState.Stale;
                health.Message = $"Using cached employee auth from {successUtc.ToLocalTime():g}. Desktop refresh is overdue.";
                return health;
            }

            health.State = WorkstationAuthCacheState.Fresh;
            health.Message = $"Employee auth synced from Desktop at {successUtc.ToLocalTime():g}.";
            return health;
        }
    }

    public sealed class WorkstationAuthCacheHealth
    {
        public WorkstationAuthCacheState State { get; set; } = WorkstationAuthCacheState.Missing;
        public string Message { get; set; } = "";
        public string LastSuccessUtc { get; set; } = "";
        public TimeSpan Age { get; set; } = TimeSpan.Zero;
        public bool AllowsOfflineLogin => State == WorkstationAuthCacheState.Fresh || State == WorkstationAuthCacheState.Stale;
    }

    public enum WorkstationAuthCacheState
    {
        Missing = 0,
        Fresh = 1,
        Stale = 2,
        Expired = 3,
    }
}
