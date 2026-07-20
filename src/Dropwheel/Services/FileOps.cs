using System.IO;
using Dropwheel.Models;

namespace Dropwheel.Services;

internal enum FileOperationStatus { Succeeded, PartiallySucceeded, Cancelled, Failed }

internal readonly record struct FileOperationCandidate(
    string Source,
    string Destination,
    bool DestinationExisted);

internal readonly record struct FileOperationChange(string Source, string Destination);

internal sealed record FileOperationResult(
    FileOperationStatus Status,
    int RequestedCount,
    int CompletedCount,
    IReadOnlyList<FileOperationChange> UndoableChanges)
{
    public bool Succeeded => Status == FileOperationStatus.Succeeded;
}

/// <summary>High-level copy, move, collision, and Recycle Bin operations.</summary>
public static class FileOps
{
    public static string[] DestinationConflicts(IEnumerable<string> files, string destinationFolder) =>
        files.Select(Path.GetFileName)
            .Where(name => !string.IsNullOrEmpty(name))
            .Select(name => Path.Combine(destinationFolder, name!))
            .Where(PathExists)
            .ToArray();

    internal static bool MoveWithoutOverwrite(string source, string destinationFolder)
    {
        var trimmedSource = Path.TrimEndingDirectorySeparator(source);
        var name = Path.GetFileName(trimmedSource);
        if (string.IsNullOrEmpty(name)) return false;
        var destination = Path.Combine(destinationFolder, name);
        try
        {
            if (File.Exists(trimmedSource))
                Microsoft.VisualBasic.FileIO.FileSystem.MoveFile(
                    trimmedSource,
                    destination,
                    overwrite: false);
            else if (Directory.Exists(trimmedSource))
                Directory.Move(trimmedSource, destination);
            else
                return false;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static bool Execute(
        IEnumerable<string> files,
        string destinationFolder,
        DropAction action,
        bool silent = false,
        ConflictPolicy policy = ConflictPolicy.Ask) =>
        ExecuteDetailed(files, destinationFolder, action, silent, policy).Succeeded;

    internal static FileOperationResult ExecuteDetailed(
        IEnumerable<string> files,
        string destinationFolder,
        DropAction action,
        bool silent = false,
        ConflictPolicy policy = ConflictPolicy.Ask)
    {
        var candidates = files.Select(source =>
        {
            var destination = Path.Combine(
                destinationFolder,
                Path.GetFileName(Path.TrimEndingDirectorySeparator(source)));
            return new FileOperationCandidate(source, destination, PathExists(destination));
        });
        return ExecuteCandidates(candidates, action, silent, policy);
    }

    public static bool ExecuteTo(
        IReadOnlyList<(string Source, string Dest)> pairs,
        DropAction action,
        bool silent = false,
        ConflictPolicy policy = ConflictPolicy.Ask) =>
        ExecuteToDetailed(pairs, action, silent, policy).Succeeded;

    internal static FileOperationResult ExecuteToDetailed(
        IReadOnlyList<(string Source, string Dest)> pairs,
        DropAction action,
        bool silent = false,
        ConflictPolicy policy = ConflictPolicy.Ask) =>
        ExecuteCandidates(
            pairs.Select(pair => new FileOperationCandidate(
                pair.Source,
                pair.Dest,
                PathExists(pair.Dest))),
            action,
            silent,
            policy);

    private static FileOperationResult ExecuteCandidates(
        IEnumerable<FileOperationCandidate> candidates,
        DropAction action,
        bool silent,
        ConflictPolicy policy)
    {
        var safeCandidates = SafeCandidates(candidates, policy);
        return safeCandidates.Length == 0
            ? SuccessfulEmptyResult()
            : ShellFileOperationBackend.Execute(safeCandidates, action, silent, policy);
    }

    private static FileOperationCandidate[] SafeCandidates(
        IEnumerable<FileOperationCandidate> candidates,
        ConflictPolicy policy)
    {
        if (policy == ConflictPolicy.KeepBoth) return candidates.ToArray();
        var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return candidates
            .Where(candidate => destinations.Add(Path.GetFullPath(candidate.Destination)))
            .ToArray();
    }

    public static uint ConflictFlags(ConflictPolicy policy) =>
        ShellFileOperationBackend.ConflictFlags(policy);

    public static bool HasDestinationCollision(
        IEnumerable<string> sources,
        string destinationFolder) =>
        DestinationConflicts(sources, destinationFolder).Length > 0;

    public static bool Delete(IEnumerable<string> paths) =>
        ShellFileOperationBackend.Delete(paths);

    internal static FileOperationResult ReconcileOutcome(
        DropAction action,
        IReadOnlyList<FileOperationCandidate> candidates,
        bool shellSucceeded,
        bool aborted)
    {
        if (shellSucceeded)
            return new FileOperationResult(
                FileOperationStatus.Succeeded,
                candidates.Count,
                candidates.Count,
                candidates.Where(candidate => !candidate.DestinationExisted)
                    .Select(candidate => new FileOperationChange(
                        candidate.Source,
                        candidate.Destination))
                    .ToArray());

        var completed = candidates
            .Where(candidate => !candidate.DestinationExisted && OperationCompleted(action, candidate))
            .Select(candidate => new FileOperationChange(candidate.Source, candidate.Destination))
            .ToArray();
        var status = completed.Length > 0
            ? FileOperationStatus.PartiallySucceeded
            : aborted ? FileOperationStatus.Cancelled : FileOperationStatus.Failed;
        return new FileOperationResult(status, candidates.Count, completed.Length, completed);
    }

    private static bool OperationCompleted(DropAction action, FileOperationCandidate candidate) =>
        PathExists(candidate.Destination)
        && (action == DropAction.Copy || !PathExists(candidate.Source));

    private static bool PathExists(string path) => File.Exists(path) || Directory.Exists(path);

    private static FileOperationResult SuccessfulEmptyResult() =>
        new(FileOperationStatus.Succeeded, 0, 0, Array.Empty<FileOperationChange>());
}
