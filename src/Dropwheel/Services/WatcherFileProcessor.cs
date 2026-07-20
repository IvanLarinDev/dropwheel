using System.IO;
using Dropwheel.Models;

namespace Dropwheel.Services;

/// <summary>UI-free routing engine for one watcher event.</summary>
internal static class WatcherFileProcessor
{
    internal static int Sort(TargetItem target, string file, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(file) && !Directory.Exists(file)) return 0;
        var moved = 0;
        foreach (var (folder, files) in SortService.MovePlan(target, new[] { file }))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (SortService.SameFolder(folder, file)) continue;
            Directory.CreateDirectory(folder);
            foreach (var source in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var success = target.ConflictPolicy switch
                {
                    ConflictPolicy.KeepBoth or ConflictPolicy.Overwrite => FileOps.Execute(
                        new[] { source }, folder, DropAction.Move, silent: true, policy: target.ConflictPolicy),
                    _ => FileOps.MoveWithoutOverwrite(source, folder),
                };
                if (success) { moved++; continue; }

                var destination = Path.Combine(folder, Path.GetFileName(Path.TrimEndingDirectorySeparator(source)));
                if (File.Exists(destination) || Directory.Exists(destination))
                    ErrorLog.Write($"Auto-sort skipped '{source}' because destination already exists: '{destination}'");
                else
                    ErrorLog.Write($"Failed to move '{source}' to '{folder}'");
            }
        }
        return moved;
    }
}
