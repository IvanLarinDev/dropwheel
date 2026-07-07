using System.IO;

namespace Dropwheel.Services;

/// <summary>Writes errors to error.log next to config.json. It exists so problems don't disappear
/// silently: the global handler, broken rules and a failed hotkey registration all leave a trace
/// here. The logger itself never throws.</summary>
public static class ErrorLog
{
    private static readonly object Gate = new();

    public static string FilePath => Path.Combine(TargetStore.Dir, "error.log");

    /// <summary>Appends a timestamped line. Write failures are swallowed on purpose — logging must
    /// not crash the app.</summary>
    public static void Write(string message, Exception? ex = null)
    {
        try
        {
            Directory.CreateDirectory(TargetStore.Dir);
            var line = ex == null
                ? $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}"
                : $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}: {ex.GetType().Name}: {ex.Message}";
            lock (Gate) File.AppendAllText(FilePath, line + Environment.NewLine);
        }
        catch { /* nowhere to write — not critical */ }
    }
}
