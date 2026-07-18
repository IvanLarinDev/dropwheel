using System.IO;
using Dropwheel.Services;

namespace Dropwheel.Tests;

public sealed class SmokeTestProtocolTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dw_smoke_" + Guid.NewGuid().ToString("N"));

    public SmokeTestProtocolTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (DirectoryNotFoundException) { }
    }

    [Fact]
    public void WriteAcknowledgement_records_exact_probe_under_isolated_profile()
    {
        var profile = Path.Combine(_root, "profile");
        var probe = Path.Combine(_root, "probe.txt");
        Directory.CreateDirectory(profile);
        File.WriteAllText(probe, "nonce");

        var acknowledgement = SmokeTestProtocol.WriteAcknowledgement(profile, probe);

        Assert.Equal(Path.Combine(profile, SmokeTestProtocol.AcknowledgementFileName), acknowledgement);
        Assert.Equal(Path.GetFullPath(probe), File.ReadAllText(acknowledgement));
    }

    [Fact]
    public void IsExpectedProbe_requires_one_exact_normalized_path()
    {
        var expected = Path.Combine(_root, "expected.txt");
        var unexpected = Path.Combine(_root, "unexpected.txt");
        File.WriteAllText(expected, "expected");
        File.WriteAllText(unexpected, "unexpected");

        Assert.True(SmokeTestProtocol.IsExpectedProbe([expected], expected));
        Assert.False(SmokeTestProtocol.IsExpectedProbe([], expected));
        Assert.False(SmokeTestProtocol.IsExpectedProbe([unexpected], expected));
        Assert.False(SmokeTestProtocol.IsExpectedProbe([unexpected, expected], expected));
    }
}
