using System.IO;

namespace Dropwheel.Services;

internal static class SmokeTestProtocol
{
    public const string AcknowledgementFileName = "smoke-ack";
    public const string DeliveryFileName = "smoke-delivery";

    public static bool IsExpectedProbe(IReadOnlyList<string> paths, string expectedProbe)
    {
        if (paths.Count != 1) return false;
        try
        {
            return string.Equals(
                Path.GetFullPath(paths[0]),
                Path.GetFullPath(expectedProbe),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    public static string WriteAcknowledgement(string profileRoot, string probePath)
    {
        var acknowledgementPath = Path.Combine(profileRoot, AcknowledgementFileName);
        File.WriteAllText(acknowledgementPath, Path.GetFullPath(probePath));
        return acknowledgementPath;
    }

    public static void WriteDeliveryMarker(string profileRoot) =>
        File.WriteAllText(Path.Combine(profileRoot, DeliveryFileName), "processed");
}
