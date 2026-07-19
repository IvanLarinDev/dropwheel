using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;

namespace Dropwheel.Services;

public static class ExplorerBridgeIpc
{
    private static readonly string PipeName = PipeNameForSession(Process.GetCurrentProcess().SessionId);
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromMilliseconds(1200);
    internal const PipeOptions ServerOptions = PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly;

    internal static string PipeNameForSession(int sessionId) => $"Dropwheel_ExplorerBridge_{sessionId}";

    public static bool TrySendFiles(IReadOnlyList<string> paths) =>
        TrySendFiles(paths, PipeName, ConnectTimeout);

    internal static bool TrySendFiles(
        IReadOnlyList<string> paths,
        string pipeName,
        TimeSpan connectTimeout)
    {
        if (paths.Count == 0) return false;

        try
        {
            using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
            pipe.Connect((int)connectTimeout.TotalMilliseconds);
            using var writer = new StreamWriter(pipe) { AutoFlush = true };
            writer.WriteLine(JsonSerializer.Serialize(paths));
            return true;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
        {
            ErrorLog.Write("Could not send Explorer bridge request to the running instance", ex);
            return false;
        }
    }

    public static Task RunServerAsync(Action<string[]> onFiles, CancellationToken token) =>
        RunServerAsync(onFiles, PipeName, token);

    internal static Task RunServerAsync(
        Action<string[]> onFiles,
        string pipeName,
        CancellationToken token) =>
        Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using var pipe = new NamedPipeServerStream(
                        pipeName,
                        PipeDirection.In,
                        maxNumberOfServerInstances: 1,
                        PipeTransmissionMode.Byte,
                        ServerOptions);
                    await pipe.WaitForConnectionAsync(token);
                    using var reader = new StreamReader(pipe);
                    var line = await reader.ReadLineAsync(token);
                    if (line == null) continue;
                    var paths = JsonSerializer.Deserialize<string[]>(line) ?? [];
                    if (paths.Length > 0) onFiles(paths);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
                {
                    ErrorLog.Write("Explorer bridge pipe request failed", ex);
                }
            }
        }, token);
}
