using RunBook.Workstation.Models;
using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace RunBook.Workstation.Services
{
    internal static class WorkstationOutboxFile
    {
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            WriteIndented = true, PropertyNameCaseInsensitive = true
        };

        public static WorkstationSyncQueueItem[] Read(string path)
        {
            try
            {
                var text = StrictUtf8.GetString(File.ReadAllBytes(path));
                if (text.Length > 0 && text[0] == '\ufeff') text = text.Substring(1);
                var items = JsonSerializer.Deserialize<WorkstationSyncQueueItem[]>(text, Options);
                if (items == null || Array.Exists(items, item => item == null))
                    throw new JsonException("The outbox must contain an event array.");
                return items;
            }
            catch (FileNotFoundException) { return Array.Empty<WorkstationSyncQueueItem>(); }
            catch (DirectoryNotFoundException) { return Array.Empty<WorkstationSyncQueueItem>(); }
            catch (Exception ex) when (ex is JsonException || ex is DecoderFallbackException || ex is IOException || ex is UnauthorizedAccessException)
            {
                throw new InvalidDataException("Saved workstation events could not be read. The original file was preserved. Ask an administrator to recover the outbox before recording or syncing more events.", ex);
            }
        }

        public static bool TryWrite(string path, WorkstationSyncQueueItem[] items, out string error)
        {
            try
            {
                Write(path, items);
                error = "";
                return true;
            }
            catch (Exception ex) when (ex is InvalidDataException || ex is IOException || ex is UnauthorizedAccessException)
            {
                error = ex is InvalidDataException ? ex.Message :
                    "Workstation events could not be saved. The existing outbox was preserved and the new action was not recorded. Resolve file access with an administrator, then retry.";
                return false;
            }
        }

        public static void Write(string path, WorkstationSyncQueueItem[] items)
        {
            // Never overwrite unreadable historical events with an empty/new queue.
            _ = Read(path);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(items, Options);
            WorkstationAtomicFile.Write(path, bytes);
        }
    }
}
