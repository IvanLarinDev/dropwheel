using Dropwheel.Services;

namespace Dropwheel.Tests;

public sealed class ExplorerBridgeServiceTests
{
    [Fact]
    public void ShortcutSpec_invokes_exe_directly_with_sendto()
    {
        var spec = ExplorerBridgeService.BuildShortcutSpec(@"C:\Program Files\Dropwheel\Dropwheel.exe");

        Assert.Equal(@"C:\Program Files\Dropwheel\Dropwheel.exe", spec.TargetPath);
        Assert.Equal("--sendto", spec.Arguments);
        Assert.Equal(@"C:\Program Files\Dropwheel", spec.WorkingDirectory);
        Assert.Equal(@"C:\Program Files\Dropwheel\Dropwheel.exe", spec.IconLocation);
    }

    [Fact]
    public void ShortcutSpec_invokes_dll_through_dotnet()
    {
        var spec = ExplorerBridgeService.BuildShortcutSpec(@"C:\Dropwheel\Dropwheel.dll");

        Assert.Equal("dotnet", spec.TargetPath);
        Assert.Equal("\"C:\\Dropwheel\\Dropwheel.dll\" --sendto", spec.Arguments);
        Assert.Equal(@"C:\Dropwheel", spec.WorkingDirectory);
        Assert.Equal(@"C:\Dropwheel\Dropwheel.dll", spec.IconLocation);
    }
}
