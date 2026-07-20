using System.IO;
using Dropwheel.Models;
using Dropwheel.Services;

namespace Dropwheel.Tests;

/// <summary>Verifies that file operations on an empty list don't call the Windows shell with a weird
/// empty pFrom, but return success right away (nothing to do).</summary>
public sealed class FileOpsTests
{
    [Theory]
    [InlineData(DropAction.Copy)]
    [InlineData(DropAction.Move)]
    public void Execute_with_no_files_is_a_noop_success(DropAction action) =>
        Assert.True(FileOps.Execute(Array.Empty<string>(), Path.GetTempPath(), action));

    [Fact]
    public void Delete_with_no_paths_is_a_noop_success() =>
        Assert.True(FileOps.Delete(Array.Empty<string>()));

    [Fact]
    public void ConflictFlags_maps_policy_to_shell_flags()
    {
        Assert.Equal(0, (int)FileOps.ConflictFlags(ConflictPolicy.Ask));
        Assert.Equal(0, (int)FileOps.ConflictFlags(ConflictPolicy.Skip)); // caller pre-filters, no shell flag
        Assert.NotEqual(0, (int)FileOps.ConflictFlags(ConflictPolicy.KeepBoth));
        Assert.NotEqual(0, (int)FileOps.ConflictFlags(ConflictPolicy.Overwrite));
        Assert.NotEqual(FileOps.ConflictFlags(ConflictPolicy.KeepBoth),
            FileOps.ConflictFlags(ConflictPolicy.Overwrite));
    }

    [Fact]
    public void TargetItem_without_a_conflict_policy_defaults_to_ask()
    {
        var t = System.Text.Json.JsonSerializer.Deserialize<TargetItem>("{\"Name\":\"X\"}");
        Assert.Equal(ConflictPolicy.Ask, t!.ConflictPolicy);
    }

    [Fact]
    public void Destination_collision_detects_existing_file_with_same_name()
    {
        var root = Path.Combine(Path.GetTempPath(), "dw_collision_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(Path.GetTempPath(), "report.txt");
            File.WriteAllText(Path.Combine(root, "report.txt"), "existing");

            Assert.True(FileOps.HasDestinationCollision(new[] { source }, root));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Destination_collision_ignores_non_colliding_names()
    {
        var root = Path.Combine(Path.GetTempPath(), "dw_collision_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Assert.False(FileOps.HasDestinationCollision(new[] { @"C:\drop\new.txt" }, root));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void MoveWithoutOverwrite_leaves_source_and_existing_destination_unchanged_on_conflict()
    {
        var root = Path.Combine(Path.GetTempPath(), "dw_no_overwrite_" + Guid.NewGuid().ToString("N"));
        var sourceDir = Path.Combine(root, "source");
        var destDir = Path.Combine(root, "dest");
        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(destDir);
        var source = Path.Combine(sourceDir, "report.txt");
        var destination = Path.Combine(destDir, "report.txt");
        File.WriteAllText(source, "source");
        File.WriteAllText(destination, "existing");
        try
        {
            Assert.False(FileOps.MoveWithoutOverwrite(source, destDir));
            Assert.Equal("source", File.ReadAllText(source));
            Assert.Equal("existing", File.ReadAllText(destination));
            Assert.Equal(new[] { "report.txt" }, Directory.GetFiles(destDir).Select(Path.GetFileName));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch (DirectoryNotFoundException) { }
        }
    }

    [Fact]
    public void MoveWithoutOverwrite_does_not_merge_an_existing_destination_directory()
    {
        var root = Path.Combine(Path.GetTempPath(), "dw_no_merge_" + Guid.NewGuid().ToString("N"));
        var sourceDir = Path.Combine(root, "source", "batch");
        var destDir = Path.Combine(root, "dest");
        var destination = Path.Combine(destDir, "batch");
        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(destination);
        var sourceOnly = Path.Combine(sourceDir, "source-only.txt");
        var existing = Path.Combine(destination, "existing.txt");
        File.WriteAllText(sourceOnly, "source");
        File.WriteAllText(existing, "existing");
        try
        {
            Assert.False(FileOps.MoveWithoutOverwrite(sourceDir, destDir));
            Assert.Equal("source", File.ReadAllText(sourceOnly));
            Assert.Equal("existing", File.ReadAllText(existing));
            Assert.False(File.Exists(Path.Combine(destination, "source-only.txt")));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch (DirectoryNotFoundException) { }
        }
    }

    [Fact]
    public void DestinationConflicts_reports_existing_destination_names()
    {
        var root = Path.Combine(Path.GetTempPath(), "dw_fileops_" + Guid.NewGuid().ToString("N"));
        var dest = Path.Combine(root, "dest");
        var src = Path.Combine(root, "src", "report.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(src)!);
        Directory.CreateDirectory(dest);
        File.WriteAllText(src, "new");
        var existing = Path.Combine(dest, "report.txt");
        File.WriteAllText(existing, "old");
        try
        {
            var conflicts = FileOps.DestinationConflicts(new[] { src }, dest);

            Assert.Equal(new[] { existing }, conflicts);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch (DirectoryNotFoundException) { }
        }
    }

    [Fact]
    public void ReconcileOutcome_reports_the_completed_subset_after_cancelled_move()
    {
        var root = Path.Combine(Path.GetTempPath(), "dw_fileop_reconcile_" + Guid.NewGuid().ToString("N"));
        var sourceDir = Directory.CreateDirectory(Path.Combine(root, "source"));
        var destinationDir = Directory.CreateDirectory(Path.Combine(root, "destination"));
        var movedSource = Path.Combine(sourceDir.FullName, "moved.txt");
        var untouchedSource = Path.Combine(sourceDir.FullName, "untouched.txt");
        var movedDestination = Path.Combine(destinationDir.FullName, "moved.txt");
        var untouchedDestination = Path.Combine(destinationDir.FullName, "untouched.txt");
        File.WriteAllText(untouchedSource, "untouched");
        File.WriteAllText(movedDestination, "moved");
        try
        {
            var result = FileOps.ReconcileOutcome(
                DropAction.Move,
                new[]
                {
                    new FileOperationCandidate(movedSource, movedDestination, DestinationExisted: false),
                    new FileOperationCandidate(untouchedSource, untouchedDestination, DestinationExisted: false),
                },
                shellSucceeded: false,
                aborted: true);

            Assert.Equal(FileOperationStatus.PartiallySucceeded, result.Status);
            Assert.Equal(1, result.CompletedCount);
            var change = Assert.Single(result.UndoableChanges);
            Assert.Equal(movedSource, change.Source);
            Assert.Equal(movedDestination, change.Destination);
        }
        finally
        {
            TempDir.Delete(root);
        }
    }
}
