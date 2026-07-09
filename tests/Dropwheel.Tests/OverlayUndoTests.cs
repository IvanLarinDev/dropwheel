using System.IO;
using Dropwheel.Models;
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
