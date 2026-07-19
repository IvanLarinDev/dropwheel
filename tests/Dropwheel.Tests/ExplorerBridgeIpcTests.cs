using System.Diagnostics;
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

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public void Smoke_sender_without_primary_fails_closed_in_isolated_profile()
    {
        var root = Path.Combine(Path.GetTempPath(), "dw smoke sender " + Guid.NewGuid().ToString("N"));
        var profile = Directory.CreateDirectory(Path.Combine(root, "profile")).FullName;
        var probe = Path.Combine(root, "probe.txt");
        File.WriteAllText(probe, "nonce");

        try
        {
            using var process = StartDropwheel("--smoke-send", profile, probe);
            var exited = process.WaitForExit(5_000);
            if (!exited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5_000);
            }

            Assert.True(exited, "The isolated smoke sender must never become a primary instance.");
            Assert.Equal(3, process.ExitCode);
            Assert.False(File.Exists(Path.Combine(profile, "config.json")));
            Assert.Contains(
                "Could not send Explorer bridge request",
                File.ReadAllText(Path.Combine(profile, "error.log")),
                StringComparison.Ordinal);
        }
        finally
        {
            TempDir.Delete(root);
        }
    }

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public void Smoke_primary_acknowledges_only_the_expected_probe()
    {
        var root = Path.Combine(Path.GetTempPath(), "dw smoke probe " + Guid.NewGuid().ToString("N"));
        var profile = Directory.CreateDirectory(Path.Combine(root, "profile")).FullName;
        var expected = Path.Combine(root, "expected.txt");
        var unexpected = Path.Combine(root, "unexpected.txt");
        File.WriteAllText(expected, "expected");
        File.WriteAllText(unexpected, "unexpected");
        var acknowledgement = Path.Combine(profile, SmokeTestProtocol.AcknowledgementFileName);
        var delivery = Path.Combine(profile, SmokeTestProtocol.DeliveryFileName);

        Process? primary = null;
        try
        {
            primary = StartDropwheel("--smoke-test", profile, expected);
            Assert.True(
                SpinWait.SpinUntil(() => File.Exists(Path.Combine(profile, "config.json")), 5_000),
                "The smoke primary did not become ready.");
            Assert.False(primary.HasExited);

            Assert.True(SendFromIsolatedProfile(profile, [unexpected, expected]));
            Assert.True(
                SpinWait.SpinUntil(() => File.Exists(delivery), 5_000),
                "The smoke primary did not process the mixed IPC payload.");
            Assert.False(File.Exists(acknowledgement));
            Assert.False(primary.HasExited);

            using (var sender = StartDropwheel("--smoke-send", profile, expected))
            {
                Assert.True(sender.WaitForExit(5_000));
                Assert.Equal(0, sender.ExitCode);
            }
            Assert.True(primary.WaitForExit(15_000));
            Assert.Equal(0, primary.ExitCode);
            Assert.Equal(Path.GetFullPath(expected), File.ReadAllText(acknowledgement));
            var errorLog = Path.Combine(profile, "error.log");
            Assert.True(!File.Exists(errorLog) || string.IsNullOrWhiteSpace(File.ReadAllText(errorLog)));
        }
        finally
        {
            if (primary is { HasExited: false })
            {
                primary.Kill(entireProcessTree: true);
                primary.WaitForExit(5_000);
            }
            primary?.Dispose();
            TempDir.Delete(root);
        }
    }

    private static Process StartDropwheel(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("dotnet") { UseShellExecute = false };
        startInfo.ArgumentList.Add(typeof(ExplorerBridgeIpc).Assembly.Location);
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start the Dropwheel process.");
    }

    private static bool SendFromIsolatedProfile(string profile, IReadOnlyList<string> paths)
    {
        var previous = TargetStore.DirOverride;
        try
        {
            TargetStore.DirOverride = profile;
            return ExplorerBridgeIpc.TrySendFiles(paths);
        }
        finally
        {
            TargetStore.DirOverride = previous;
        }
    }
}
