using System.IO;

namespace Dropwheel.Services;

/// <summary>Writes errors to error.log next to config.json. It exists so problems don't disappear
/// silently: the global handler, broken rules and a failed hotkey registration all leave a trace
/// here. The logger itself never throws.</summary>
public static class ErrorLog
{
    private static readonly object Gate = new();

    public static string FilePath => Path.Combine(TargetStore.Dir, "error.log");

    /// <summary>Path of the diagnostic event trace, separate from errors so it can be flooded with
    /// lifecycle events (wheel open/close, hover, proximity, timers) without burying real errors.</summary>
    public static string TraceFilePath => Path.Combine(TargetStore.Dir, "trace.log");

    /// <summary>Appends a millisecond-stamped diagnostic event to trace.log. Same swallow-everything
    /// contract as <see cref="Write"/>: tracing must never crash or slow the app noticeably.</summary>
    public static void Trace(string message)
    {
        try
        {
            Directory.CreateDirectory(TargetStore.Dir);
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {message}";
            lock (Gate) File.AppendAllText(TraceFilePath, line + Environment.NewLine);
        }
        catch { /* nowhere to write — not critical */ }
    }

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
