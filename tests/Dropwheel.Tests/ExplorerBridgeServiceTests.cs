using Dropwheel.Services;

namespace Dropwheel.Tests;

public sealed class ExplorerBridgeServiceTests
{
    [Fact]
    public void LauncherText_invokes_exe_with_sendto_and_original_arguments()
    {
        var text = ExplorerBridgeService.LauncherText(@"C:\Program Files\Dropwheel\Dropwheel.exe");

        Assert.Contains("\"C:\\Program Files\\Dropwheel\\Dropwheel.exe\" --sendto %*", text);
    }

    [Fact]
    public void LauncherText_invokes_dll_through_dotnet()
    {
        var text = ExplorerBridgeService.LauncherText(@"C:\Dropwheel\Dropwheel.dll");

        Assert.Contains("dotnet \"C:\\Dropwheel\\Dropwheel.dll\" --sendto %*", text);
    }
}
