using System;
using System.IO;

namespace RunBook.Workstation.Services
{
    internal static class WorkstationAtomicFile
    {
        public static void Write(string path, byte[] bytes)
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
            var temporary = Path.Combine(directory, "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(flushToDisk: true);
                }

                // Same-directory rename preserves the existing destination if publication fails.
                File.Move(temporary, path, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
        }
    }
}
