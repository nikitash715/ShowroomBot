namespace ShowroomBot.Core;

public static class DiagnosticsCleanup
{
    public static void Clean(string directoryPath, DateTime? utcNow = null)
    {
        var cutoff = (utcNow ?? DateTime.UtcNow).AddDays(-1);
        CleanDirectory(new DirectoryInfo(Path.GetFullPath(directoryPath)), cutoff, deleteWhenEmpty: false);
    }

    private static void CleanDirectory(DirectoryInfo directory, DateTime cutoff, bool deleteWhenEmpty)
    {
        try
        {
            if (!directory.Exists || (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                return;

            // Capture before deleting children, which changes the directory's modification time.
            var lastWriteUtc = directory.LastWriteTimeUtc;
            foreach (var entry in directory.GetFileSystemInfos())
            {
                try
                {
                    // Never follow links outside the diagnostics directory.
                    if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                        continue;
                    if (entry is DirectoryInfo child)
                        CleanDirectory(child, cutoff, deleteWhenEmpty: true);
                    else if (entry.LastWriteTimeUtc < cutoff)
                        entry.Delete();
                }
                catch (IOException) { /* A locked file must not prevent startup or other cleanup. */ }
                catch (UnauthorizedAccessException) { /* Leave inaccessible diagnostics in place. */ }
            }

            if (deleteWhenEmpty && lastWriteUtc < cutoff && !directory.EnumerateFileSystemInfos().Any())
                directory.Delete();
        }
        catch (IOException) { /* Cleanup is best effort, including directories removed concurrently. */ }
        catch (UnauthorizedAccessException) { /* Cleanup must not prevent application startup. */ }
    }
}
