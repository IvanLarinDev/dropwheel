using System.IO;
using Dropwheel.Services;

namespace Dropwheel.Tests;

[Collection("WindowsIntegration")]
public sealed class ExplorerBridgeIpcTests
{
    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public async Task Named_pipe_round_trip_delivers_paths_and_stops_cleanly()
    {
        var root = Path.Combine(Path.GetTempPath(), "dw_ipc_" + Guid.NewGuid().ToString("N"));
        var payloadDir = Directory.CreateDirectory(Path.Combine(root, "Папка с пробелом"));
        var first = Path.Combine(payloadDir.FullName, "один файл.txt");
        var second = Path.Combine(payloadDir.FullName, "two files.txt");
        File.WriteAllText(first, "one");
        File.WriteAllText(second, "two");
        var pipeName = "Dropwheel_Test_" + Guid.NewGuid().ToString("N");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var received = new TaskCompletionSource<string[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = ExplorerBridgeIpc.RunServerAsync(
            paths => received.TrySetResult(paths),
            pipeName,
            cts.Token);
        try
        {
            var sent = ExplorerBridgeIpc.TrySendFiles(
                new[] { first, second },
                pipeName,
                TimeSpan.FromSeconds(2));

            Assert.True(sent);
            Assert.Equal(
                new[] { first, second },
                await received.Task.WaitAsync(TimeSpan.FromSeconds(2)));
        }
        finally
        {
            try
            {
                await cts.CancelAsync();
                await server.WaitAsync(TimeSpan.FromSeconds(2));
            }
            finally
            {
                TempDir.Delete(root);
            }
        }
    }
}
