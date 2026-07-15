using System.IO;

namespace Dropwheel.Tests;

/// <summary>Removes a test's temp directory, riding out a transient lock. A test outside the
/// TargetStoreState collection can still be writing error.log into the shared DirOverride folder when a
/// collection test's teardown runs, so a plain Directory.Delete occasionally throws IOException. A short
/// widening retry lets that write finish. A missing directory is treated as already gone.</summary>
internal static class TempDir
{
    public static void Delete(string path)
    {
        for (int attempt = 1; ; attempt++)
        {
            try { Directory.Delete(path, true); return; }
            catch (DirectoryNotFoundException) { return; }
            catch (Exception ex) when (attempt < 8 && ex is IOException or UnauthorizedAccessException)
            {
                System.Threading.Thread.Sleep(50 * attempt);
            }
        }
    }
}
