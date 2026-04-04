using System;
using System.IO;

namespace RunBook.Workstation.Services
{
    public static class DebugLogService
    {
        private static readonly object Gate = new object();
        private static string LogPath => Path.Combine(WorkstationStorageService.LogsFolder, $"workstation_{DateTime.UtcNow:yyyyMMdd}.log");

        public static void Write(string message)
        {
            try
            {
                WorkstationStorageService.EnsureRuntimeFolders();
                lock (Gate)
                {
                    File.AppendAllText(LogPath, $"[{DateTime.UtcNow:O}] {message}{Environment.NewLine}");
                }
            }
            catch
            {
            }
        }

        public static void WriteException(string context, Exception ex)
        {
            Write(context + " | " + ex.Message + Environment.NewLine + ex.StackTrace);
        }
    }
}
