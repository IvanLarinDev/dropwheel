using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;

namespace Dropwheel.Services;

public static class ExplorerBridgeIpc
{
    private static readonly string PipeName = PipeNameForSession(Process.GetCurrentProcess().SessionId);
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(3);
    private const int SendAttempts = 4;
    private const byte Acknowledged = 0x06;
    internal const int MaxRequestBytes = 1024 * 1024;
    internal const int MaxPathCount = 2048;
    internal const int MaxPathChars = 32767;
    internal const PipeOptions ServerOptions = PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly;

    internal static string PipeNameForSession(int sessionId) => $"Dropwheel_ExplorerBridge_{sessionId}";

    public static bool TrySendFiles(IReadOnlyList<string> paths)
    {
        if (!TryEncodeRequest(paths, out var payload)) return false;
        Exception? lastFailure = null;
        for (var attempt = 1; attempt <= SendAttempts; attempt++)
        {
            if (TrySendPayload(payload, PipeName, ConnectTimeout, out lastFailure)) return true;
            if (attempt < SendAttempts) Thread.Sleep(100 * attempt);
        }
        ErrorLog.Write("Could not send Explorer bridge request to the running instance", lastFailure);
        return false;
    }

    internal static bool TrySendFiles(
        IReadOnlyList<string> paths,
        string pipeName,
        TimeSpan connectTimeout)
    {
        if (!TryEncodeRequest(paths, out var payload)) return false;
        var sent = TrySendPayload(payload, pipeName, connectTimeout, out var failure);
        if (!sent && failure != null)
            ErrorLog.Write("Could not send Explorer bridge request to the running instance", failure);
        return sent;
    }

    private static bool TrySendPayload(
        byte[] payload,
        string pipeName,
        TimeSpan connectTimeout,
        out Exception? failure)
    {
        failure = null;
        try
        {
            using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            pipe.Connect((int)connectTimeout.TotalMilliseconds);
            var header = new byte[sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
            using var requestTimeout = new CancellationTokenSource(RequestTimeout);
            pipe.WriteAsync(header, requestTimeout.Token).AsTask().GetAwaiter().GetResult();
            pipe.WriteAsync(payload, requestTimeout.Token).AsTask().GetAwaiter().GetResult();
            pipe.FlushAsync(requestTimeout.Token).GetAwaiter().GetResult();

            var ack = new byte[1];
            var read = pipe.ReadAsync(ack, requestTimeout.Token).AsTask().GetAwaiter().GetResult();
            return read == 1 && ack[0] == Acknowledged;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException
            or OperationCanceledException)
        {
            failure = ex;
            return false;
        }
    }

    internal static bool TryEncodeRequest(IReadOnlyList<string> paths, out byte[] payload)
    {
        payload = [];
        if (paths.Count == 0 || paths.Count > MaxPathCount
            || paths.Any(path => string.IsNullOrWhiteSpace(path) || path.Length > MaxPathChars))
            return false;

        payload = JsonSerializer.SerializeToUtf8Bytes(paths);
        if (payload.Length > MaxRequestBytes)
        {
            payload = [];
            return false;
        }
        return true;
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
                        PipeDirection.InOut,
                        maxNumberOfServerInstances: 1,
                        PipeTransmissionMode.Byte,
                        ServerOptions);
                    await pipe.WaitForConnectionAsync(token);
                    using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                    requestTimeout.CancelAfter(RequestTimeout);
                    var paths = await ReadRequestAsync(pipe, requestTimeout.Token);
                    onFiles(paths);
                    await pipe.WriteAsync(new[] { Acknowledged }, requestTimeout.Token);
                    await pipe.FlushAsync(requestTimeout.Token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException
                    or UnauthorizedAccessException or OperationCanceledException)
                {
                    ErrorLog.Write("Explorer bridge pipe request failed", ex);
                }
            }
        }, token);

    private static async Task<string[]> ReadRequestAsync(Stream pipe, CancellationToken token)
    {
        var header = new byte[sizeof(int)];
        await pipe.ReadExactlyAsync(header, token);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length <= 0 || length > MaxRequestBytes)
            throw new InvalidDataException("Explorer bridge request length is outside the allowed range.");

        var payload = new byte[length];
        await pipe.ReadExactlyAsync(payload, token);
        var paths = JsonSerializer.Deserialize<string[]>(payload) ?? [];
        if (!TryEncodeRequest(paths, out _))
            throw new InvalidDataException("Explorer bridge request contains invalid paths.");
        return paths;
    }
}
