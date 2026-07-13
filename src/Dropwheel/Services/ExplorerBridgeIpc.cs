using System.IO;
using System.IO.Pipes;
using System.Text.Json;

namespace Dropwheel.Services;

public static class ExplorerBridgeIpc
{
    private const string PipeName = "Dropwheel_ExplorerBridge";
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromMilliseconds(1200);

    public static bool TrySendFiles(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) return false;

        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            pipe.Connect((int)ConnectTimeout.TotalMilliseconds);
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
        Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using var pipe = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.In,
                        maxNumberOfServerInstances: 1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);
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
