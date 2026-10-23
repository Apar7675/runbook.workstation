using RunBook.Workstation.Models;
using System;
using System.Globalization;
using System.IO;

namespace RunBook.Workstation.Services
{
    // A queued event belongs to its originating actor and device, never the next kiosk user.
    internal static class WorkstationQueuePolicy
    {
        public static bool BelongsToSession(WorkstationSyncQueueItem item, WorkstationSessionSnapshot session)
        {
            if (item == null || session?.Employee == null ||
                !MatchesDeviceScope(session, item.ShopId, item.WorkstationId) ||
                !SameRequired(item.DeviceId, session.WorkstationId))
                return false;

            if (!int.TryParse(item.EmployeeId, NumberStyles.None, CultureInfo.InvariantCulture, out var itemEmployee) ||
                !int.TryParse(session.Employee.EmployeeId, NumberStyles.None, CultureInfo.InvariantCulture, out var sessionEmployee) ||
                itemEmployee <= 0 || itemEmployee != sessionEmployee)
                return false;

            return SameOptional(item.RemoteEmployeeId, session.Employee.RemoteEmployeeId) &&
                   SameOptional(item.EmployeeCode, session.Employee.EmployeeCode);
        }

        public static bool MatchesDeviceScope(WorkstationSessionSnapshot session, string? shopId, string? workstationId)
            => session != null && SameRequired(shopId, session.ShopId) &&
               SameRequired(workstationId, session.WorkstationId);

        private static bool SameRequired(string? value, string? expected)
            => !string.IsNullOrWhiteSpace(value) && !string.IsNullOrWhiteSpace(expected) &&
               string.Equals(value.Trim(), expected.Trim(), StringComparison.OrdinalIgnoreCase);

        private static bool SameOptional(string? value, string? expected)
            => string.IsNullOrWhiteSpace(value) || SameRequired(value, expected);

        public static void ClearDisposableCache(string folder)
        {
            if (!Directory.Exists(folder))
                return;

            // Do not recursively delete a shop folder: it contains the durable outbox.
            // Retain queues (including unreadable ones), unknown files, and their original bytes.
            foreach (var name in new[] {
                "timeclock-cache.json", "workstation-session.dat", "registration.dat",
                "registration.json", "timeclock-state.json", "employee-auth-cache.dat",
                "runtime-access-token.dat" })
            {
                var path = Path.Combine(folder, name);
                if (File.Exists(path))
                    File.Delete(path);
            }
        }
    }
}
