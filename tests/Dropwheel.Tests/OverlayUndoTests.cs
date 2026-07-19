using System.IO;
using Dropwheel.Models;
using Dropwheel.Services;
using Dropwheel.UI;

namespace Dropwheel.Tests;

public sealed class OverlayUndoTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dw_undo_" + Guid.NewGuid().ToString("N"));

    public OverlayUndoTests() => Directory.CreateDirectory(_root);
    public void Dispose() { try { Directory.Delete(_root, true); } catch (DirectoryNotFoundException) { } }

    [Fact]
    public void CopyUndoTargets_skips_destination_that_existed_before_copy()
    {
        var src = Path.Combine(_root, "src", "report.txt");
        var dest = Path.Combine(_root, "dest");
        Directory.CreateDirectory(Path.GetDirectoryName(src)!);
        Directory.CreateDirectory(dest);
        File.WriteAllText(src, "new");
        var existing = Path.Combine(dest, "report.txt");
        File.WriteAllText(existing, "old");
        var op = OverlayWindow.BuildOpBefore(DropAction.Copy, new[] { src }, dest);

        var targets = OverlayWindow.CopyUndoTargets(op);

        Assert.Empty(targets);
    }

    [Fact]
    public void CopyUndoTargets_returns_created_copy_when_no_prior_conflict_exists()
    {
        var src = Path.Combine(_root, "src", "report.txt");
        var dest = Path.Combine(_root, "dest");
        Directory.CreateDirectory(Path.GetDirectoryName(src)!);
        Directory.CreateDirectory(dest);
        File.WriteAllText(src, "new");
        var op = OverlayWindow.BuildOpBefore(DropAction.Copy, new[] { src }, dest);
        var copy = Path.Combine(dest, "report.txt");
        File.WriteAllText(copy, "new");

        var targets = OverlayWindow.CopyUndoTargets(op);

        Assert.Equal(new[] { copy }, targets);
    }

    [Fact]
    public void UndoOne_copy_reports_incomplete_when_destination_preexisted()
    {
        var src = Path.Combine(_root, "src", "report.txt");
        var dest = Path.Combine(_root, "dest");
        Directory.CreateDirectory(Path.GetDirectoryName(src)!);
        Directory.CreateDirectory(dest);
        File.WriteAllText(src, "new");
        File.WriteAllText(Path.Combine(dest, "report.txt"), "old");
        var op = OverlayWindow.BuildOpBefore(DropAction.Copy, new[] { src }, dest);

        Assert.False(OverlayWindow.UndoOne(op));
    }

    [Fact]
    public void RenamedFileName_expands_template_and_keeps_the_extension()
    {
        var src = Path.Combine(_root, "report.pdf");
        var name = OverlayWindow.RenamedFileName("archive-${date}-${stem}", src, new DateTime(2026, 7, 14));
        Assert.Equal("archive-2026-07-14-report.pdf", name);
    }

    [Fact]
    public void RenamedFileName_falls_back_to_the_original_name_when_unfillable()
    {
        var src = Path.Combine(_root, "report.pdf");
        // ${nope} is neither a built-in token nor a regex group → unfillable → keep the original name
        var name = OverlayWindow.RenamedFileName("${nope}", src, new DateTime(2026, 7, 14));
        Assert.Equal("report.pdf", name);
    }

    [Fact]
    public void CopyUndoTargets_uses_explicit_destination_paths_for_a_renamed_drop()
    {
        var src = Path.Combine(_root, "src", "report.pdf");
        var dest = Path.Combine(_root, "dest");
        Directory.CreateDirectory(Path.GetDirectoryName(src)!);
        Directory.CreateDirectory(dest);
        File.WriteAllText(src, "new");
        var renamed = Path.Combine(dest, "archive-report.pdf");
        var op = OverlayWindow.BuildRenamedOp(DropAction.Copy, new[] { src }, dest, new[] { renamed });
        File.WriteAllText(renamed, "new"); // the copy the drop created

        Assert.Equal(new[] { renamed }, OverlayWindow.CopyUndoTargets(op));
    }

    [Fact]
    public void Partial_copy_undo_targets_only_confirmed_destinations()
    {
        var completedSource = Path.Combine(_root, "src", "completed.txt");
        var untouchedSource = Path.Combine(_root, "src", "untouched.txt");
        var completedDestination = Path.Combine(_root, "dest", "completed.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(completedSource)!);
        Directory.CreateDirectory(Path.GetDirectoryName(completedDestination)!);
        File.WriteAllText(completedSource, "completed");
        File.WriteAllText(untouchedSource, "untouched");
        File.WriteAllText(completedDestination, "completed");
        var op = OverlayWindow.BuildPartialOp(
            DropAction.Copy,
            new[] { new FileOperationChange(completedSource, completedDestination) });

        Assert.Equal(new[] { completedDestination }, OverlayWindow.CopyUndoTargets(op));
    }

    [Fact]
    public void UndoOne_move_reports_incomplete_when_destination_preexisted()
    {
        var src = Path.Combine(_root, "src", "report.txt");
        var dest = Path.Combine(_root, "dest");
        Directory.CreateDirectory(Path.GetDirectoryName(src)!);
        Directory.CreateDirectory(dest);
        File.WriteAllText(src, "new");
        File.WriteAllText(Path.Combine(dest, "report.txt"), "old");
        var op = OverlayWindow.BuildOpBefore(DropAction.Move, new[] { src }, dest);

        Assert.False(OverlayWindow.UndoOne(op));
    }
}
