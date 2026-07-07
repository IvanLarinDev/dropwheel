using System.Collections.Concurrent;
using System.IO;
using System.Windows.Threading;
using Dropwheel.Models;

namespace Dropwheel.Services;

/// <summary>Watches sorter folders whose Watch flag is on and routes files that appear in them by the
/// same rules. Not recursive: a file moved into a subfolder raises no new event, so there is no loop.
/// A file that resolves to its own folder (no rule match, or an unfilled token) is left untouched.
/// Auto-sort moves files silently and is not tracked by Undo.</summary>
public sealed class WatcherService
{
    private const int PollMs = 1000;
    private const int MaxWaitTicks = 600; // up to ~10 minutes waiting for a large file to be released

    private sealed class Entry
    {
        public required FileSystemWatcher Watcher { get; init; }
        public TargetItem Target { get; set; } = null!;
    }

    private readonly Dispatcher _ui;
    private readonly Action<int> _notifySorted;
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _inFlight = new(StringComparer.OrdinalIgnoreCase);
    // Bound concurrent moves: a bulk dump of hundreds of files must not spawn hundreds of threads
    // blocked on SHFileOperation and thrash the disk against each other.
    private readonly SemaphoreSlim _moveGate = new(Math.Max(2, Environment.ProcessorCount / 2));
    private readonly DispatcherTimer _toastTimer;
    private int _pendingCount;

    public WatcherService(Dispatcher ui, Action<int> notifySorted)
    {
        _ui = ui;
        _notifySorted = notifySorted;
        _toastTimer = new DispatcherTimer(DispatcherPriority.Background, _ui)
        { Interval = TimeSpan.FromSeconds(2) };
        _toastTimer.Tick += OnToastTick;
    }

    /// <summary>Starts watching and subscribes to config saves so toggling the Watch flag takes
    /// effect without a restart.</summary>
    public void Start()
    {
        TargetStore.Saved += Rebuild;
        Rebuild();
    }

    public void Stop()
    {
        TargetStore.Saved -= Rebuild;
        foreach (var e in _entries.Values) e.Watcher.Dispose();
        _entries.Clear();
    }

    /// <summary>Re-syncs watchers to the current set of watched sorter folders: adds new folders,
    /// drops removed ones, and refreshes the target reference for folders that stay — so rule edits
    /// take effect without recreating the watcher.</summary>
    private void Rebuild()
    {
        var desired = new Dictionary<string, TargetItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in TargetStore.AllTargets)
        {
            if (!t.Watch || !t.IsSorter) continue;
            string path;
            try { path = Path.GetFullPath(t.Path); }
            catch { continue; } // invalid path in config — skip, do not throw
            if (!Directory.Exists(path)) continue;
            desired[path] = t; // rare duplicate (two targets on one folder) — keep the last
        }

        foreach (var path in _entries.Keys.Where(p => !desired.ContainsKey(p)).ToList())
        {
            _entries[path].Watcher.Dispose();
            _entries.Remove(path);
        }

        foreach (var (path, target) in desired)
        {
            if (_entries.TryGetValue(path, out var existing)) { existing.Target = target; continue; }
            // One bad folder (path too long, network glitch) must not break the whole rebuild — it
            // runs from the config-save handler.
            try
            {
                var entry = new Entry { Watcher = CreateWatcher(path) };
                entry.Target = target;
                entry.Watcher.Created += (_, e) => OnAppeared(entry, e.FullPath);
                entry.Watcher.Renamed += (_, e) => OnAppeared(entry, e.FullPath);
                entry.Watcher.Error += (_, e) => OnError(entry, path, e.GetException());
                entry.Watcher.EnableRaisingEvents = true;
                _entries[path] = entry;
            }
            catch (Exception ex) { ErrorLog.Write($"Failed to start watching folder '{path}'", ex); }
        }
    }

    private static FileSystemWatcher CreateWatcher(string path) => new(path)
    {
        IncludeSubdirectories = false,
        NotifyFilter = NotifyFilters.FileName,
        // Larger buffer — fewer lost events when many files are dropped at once (64 KB is the max).
        InternalBufferSize = 64 * 1024,
    };

    /// <summary>On buffer overflow some Created events are lost (a bulk dump of hundreds of files),
    /// so instead of only logging the error we re-scan the whole folder and re-process everything in
    /// it now. The _inFlight dedup keeps a file from being processed twice.</summary>
    private void OnError(Entry entry, string path, Exception ex)
    {
        ErrorLog.Write($"Watch buffer overflow for folder '{path}' — rescanning", ex);
        Sweep(entry);
    }

    /// <summary>Queues every top-level file in the folder. Used to recover after a buffer overflow:
    /// files whose event was lost are picked up on the rescan.</summary>
    private void Sweep(Entry entry)
    {
        string[] files;
        try { files = Directory.GetFiles(entry.Watcher.Path); }
        catch (Exception ex) { ErrorLog.Write($"Failed to rescan folder '{entry.Watcher.Path}'", ex); return; }
        foreach (var f in files) OnAppeared(entry, f);
    }

    /// <summary>The event arrives on a thread-pool thread. A given path is processed once (Created and
    /// Renamed can both fire for one file); then we wait off-thread for it to be released.</summary>
    private void OnAppeared(Entry entry, string fullPath)
    {
        if (!_inFlight.TryAdd(fullPath, 0)) return;
        _ = ProcessWhenReady(entry, fullPath);
    }

    private async Task ProcessWhenReady(Entry entry, string file)
    {
        try
        {
            for (int i = 0; i < MaxWaitTicks; i++)
            {
                if (Directory.Exists(file)) return;   // a folder appeared, not a file
                if (!File.Exists(file)) return;       // the file vanished while we waited
                if (IsReady(file)) break;             // the writing process released it — fully written
                if (i == MaxWaitTicks - 1)
                {
                    ErrorLog.Write($"File '{file}' stays locked — auto-sort skipped");
                    return;
                }
                await Task.Delay(PollMs);
            }
            await _moveGate.WaitAsync();
            try { SortOne(entry, file); } // off-thread: planning and the silent move never touch the UI
            finally { _moveGate.Release(); }
        }
        catch (Exception ex) { ErrorLog.Write($"Error waiting for file '{file}'", ex); }
        finally { _inFlight.TryRemove(file, out _); }
    }

    /// <summary>A file counts as fully written once it can be opened exclusively: while a copy or
    /// download is in progress the writer keeps it locked.</summary>
    private static bool IsReady(string file)
    {
        try
        {
            using var s = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private void SortOne(Entry entry, string file)
    {
        try
        {
            if (!File.Exists(file)) return;
            var plan = SortService.Plan(entry.Target, new[] { file });
            foreach (var (folder, files) in plan)
            {
                if (SameFolder(folder, file)) continue; // file stays in its own folder — no move, no loop
                // Create the destination folder first: otherwise SHFileOperation moving a single file
                // to a non-existent path treats the last segment as a new file name, not a folder.
                Directory.CreateDirectory(folder);
                if (FileOps.Execute(files, folder, DropAction.Move, silent: true))
                    _ui.InvokeAsync(() => QueueToast(files.Count)); // coalesce the toast on the UI thread
                else
                    ErrorLog.Write($"Failed to move '{file}' to '{folder}'");
            }
        }
        catch (Exception ex) { ErrorLog.Write($"Auto-sort of '{file}' failed", ex); }
    }

    /// <summary>The destination folder is the file's own folder. Then there is nothing to move and,
    /// more importantly, moving into the same folder could make the watcher loop.</summary>
    public static bool SameFolder(string destFolder, string file)
    {
        var src = Path.GetDirectoryName(Path.GetFullPath(file));
        if (src == null) return false;
        return string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(destFolder)),
            Path.TrimEndingDirectorySeparator(src),
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Coalesces sorted files into one toast: a burst of many files collapses into a single
    /// notification after a short pause instead of one toast per file.</summary>
    private void QueueToast(int count)
    {
        _pendingCount += count;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private void OnToastTick(object? sender, EventArgs e)
    {
        _toastTimer.Stop();
        int n = _pendingCount;
        _pendingCount = 0;
        if (n > 0) _notifySorted(n);
    }
}
