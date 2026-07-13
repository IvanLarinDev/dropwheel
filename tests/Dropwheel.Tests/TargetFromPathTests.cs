using System.IO;
using Dropwheel.UI;

namespace Dropwheel.Tests;

/// <summary>Covers the naming fallback chain when a dropped/captured path becomes a target. A non-.lnk
/// path is returned unchanged by ShortcutResolver, so these exercise pure naming logic.</summary>
public sealed class TargetFromPathTests
{
    [Fact]
    public void Uses_the_file_name_without_extension()
    {
        var t = OverlayWindow.TargetFromPath(@"C:\apps\Visual Studio Code.exe");

        Assert.Equal("Visual Studio Code", t.Name);
        Assert.Equal(@"C:\apps\Visual Studio Code.exe", t.Path);
    }

    [Fact]
    public void Uses_the_folder_leaf_name_for_a_folder_path()
    {
        var t = OverlayWindow.TargetFromPath(@"C:\Users\me\Downloads");

        Assert.Equal("Downloads", t.Name);
    }

    [Fact]
    public void Falls_back_to_the_full_path_when_there_is_no_leaf_name()
    {
        var t = OverlayWindow.TargetFromPath(@"C:\");

        Assert.Equal(@"C:\", t.Path);
        Assert.False(string.IsNullOrEmpty(t.Name));
    }

    [Fact]
    public void Explorer_sendto_auto_adds_directory_targets()
    {
        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        try
        {
            Assert.True(OverlayWindow.ShouldAutoAddExplorerTargets([dir.FullName]));
        }
        finally
        {
            dir.Delete();
        }
    }

    [Fact]
    public void Explorer_sendto_keeps_plain_files_as_payload()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(path, "payload");
        try
        {
            Assert.False(OverlayWindow.ShouldAutoAddExplorerTargets([path]));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
