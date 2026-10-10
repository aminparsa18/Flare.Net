namespace Flare.Maui;

/// <summary>
/// Bounds the disk-retry directory. The OpenTelemetry exporter writes failed batches there and its own retention
/// is not configurable, so Flare trims the directory at startup: files older than a maximum age go, then the
/// oldest remaining ones until the total fits a size cap.
/// </summary>
internal static class OfflineQueue
{
    internal static void Prune(string directory, long maxBytes, TimeSpan maxAge, DateTime nowUtc)
    {
        try
        {
            if (!Directory.Exists(directory)) return;
            var files = new DirectoryInfo(directory).EnumerateFiles("*", SearchOption.AllDirectories)
                .OrderBy(f => f.LastWriteTimeUtc)
                .ToList();

            var total = files.Sum(f => f.Length);
            foreach (var file in files)
            {
                var expired = nowUtc - file.LastWriteTimeUtc > maxAge;
                if (!expired && total <= maxBytes) break;
                try
                {
                    var size = file.Length;
                    file.Delete();
                    total -= size;
                }
                catch (IOException) { /* in use or already gone: leave it */ }
                catch (UnauthorizedAccessException) { }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A queue that cannot be trimmed must not stop the app from starting.
        }
    }
}
