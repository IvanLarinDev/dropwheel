using System.IO;
using Dropwheel.UI;

namespace Dropwheel.Tests;

public sealed class OverlaySortTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dw_sort_overlay_" + Guid.NewGuid().ToString("N"));

    public OverlaySortTests() => Directory.CreateDirectory(_root);
    public void Dispose() { try { Directory.Delete(_root, true); } catch (DirectoryNotFoundException) { } }

    [Fact]
    public void ExecutableSorterGroups_skips_sources_already_in_destination_folder()
    {
        var inRoot = Path.Combine(_root, "already-here.txt");
        var incomingDir = Path.Combine(_root, "incoming");
        var incoming = Path.Combine(incomingDir, "move-me.txt");
        Directory.CreateDirectory(incomingDir);
        File.WriteAllText(inRoot, "same-folder");
        File.WriteAllText(incoming, "incoming");
        var plan = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [_root] = new() { inRoot, incoming },
        };

        var groups = OverlayWindow.ExecutableSorterGroups(plan);

        var group = Assert.Single(groups);
        Assert.Equal(_root, group.Folder);
        Assert.Equal(new[] { incoming }, group.Sources);
    }
}
