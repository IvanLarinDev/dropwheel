using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Threading;
using Dropwheel.Models;
using Dropwheel.Services;

namespace Dropwheel.Tests;

[Collection("WindowsIntegration")]
public sealed class WatcherServiceIntegrationTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "dw_watcher_" + Guid.NewGuid().ToString("N"));

    public WatcherServiceIntegrationTests() => Directory.CreateDirectory(_root);

    public void Dispose() => TempDir.Delete(_root);

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public async Task Real_watcher_routes_a_burst_and_cancels_locked_work_on_stop()
    {
        const int fileCount = 16;
        var watchedFolder = Directory.CreateDirectory(Path.Combine(_root, "watched")).FullName;
        var destinationFolder = Path.Combine(watchedFolder, "Images");
        var target = new TargetItem
        {
            Path = watchedFolder,
            Watch = true,
            Rules =
            [
                new SortRule
                {
                    Dest = "Images",
                    All =
                    {
                        new RuleCondition
                        {
                            Field = ConditionField.Extension,
                            Op = CompareOp.In,
                            Value = "jpg",
                        },
                    },
                },
            ],
        };
        var isolatedProfile = Directory.CreateDirectory(Path.Combine(_root, "profile")).FullName;
        var previousTargets = TargetStore.Config.Targets.ToArray();
        var previousDirOverride = TargetStore.DirOverride;
        WatcherService? service = null;

        try
        {
            TargetStore.DirOverride = isolatedProfile;
            Assert.Equal(isolatedProfile, TargetStore.Dir, ignoreCase: true);
            TargetStore.Config.Targets.Clear();
            TargetStore.Config.Targets.Add(target);
            service = new WatcherService(Dispatcher.CurrentDispatcher, _ => { });
            service.Start();
            var fileNames = Enumerable.Range(0, fileCount)
                .Select(index => $"burst-{index:D2}.jpg")
                .ToArray();
            foreach (var fileName in fileNames)
                File.WriteAllText(Path.Combine(watchedFolder, fileName), fileName);

            await AssertEventuallyAsync(
                () => fileNames.All(fileName => File.Exists(Path.Combine(destinationFolder, fileName))),
                TimeSpan.FromSeconds(20),
                "The real watcher did not route every file in the burst.");

            var cancelledPath = Path.Combine(watchedFolder, "cancelled.jpg");
            using (var locked = new FileStream(
                cancelledPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None))
            {
                locked.WriteByte(0x2a);
                locked.Flush(flushToDisk: true);
                await AssertEventuallyAsync(
                    () => IsInFlight(service, cancelledPath),
                    TimeSpan.FromSeconds(10),
                    "The real watcher did not observe the locked file.");

                service.Stop();

                await AssertEventuallyAsync(
                    () => !IsInFlight(service, cancelledPath),
                    TimeSpan.FromSeconds(10),
                    "Stopping the watcher did not cancel its in-flight work.");
            }

            Assert.True(File.Exists(cancelledPath));
            Assert.False(File.Exists(Path.Combine(destinationFolder, "cancelled.jpg")));
        }
        finally
        {
            try
            {
                if (service is not null)
                {
                    service.Stop();
                    await AssertEventuallyAsync(
                        () => IsIdle(service),
                        TimeSpan.FromSeconds(10),
                        "Stopping the watcher did not drain all in-flight work.");
                }
            }
            finally
            {
                TargetStore.Config.Targets.Clear();
                TargetStore.Config.Targets.AddRange(previousTargets);
                TargetStore.DirOverride = previousDirOverride;
            }
        }
    }

    private static bool IsInFlight(WatcherService service, string path)
        => GetInFlight(service).ContainsKey(path);

    private static bool IsIdle(WatcherService service) => GetInFlight(service).IsEmpty;

    private static ConcurrentDictionary<string, byte> GetInFlight(WatcherService service)
    {
        var field = typeof(WatcherService).GetField("_inFlight", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("WatcherService._inFlight not found.");
        return (ConcurrentDictionary<string, byte>?)field.GetValue(service)
            ?? throw new InvalidOperationException("WatcherService._inFlight is not initialized.");
    }

    private static async Task AssertEventuallyAsync(
        Func<bool> condition,
        TimeSpan timeout,
        string failureMessage)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            if (condition()) return;
            await Task.Delay(50);
        }

        Assert.True(condition(), failureMessage);
    }
}
