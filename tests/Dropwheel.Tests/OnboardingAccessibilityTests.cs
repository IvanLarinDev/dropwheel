using System.IO;
using System.Reflection;
using System.Xml.Linq;

namespace Dropwheel.Tests;

/// <summary>Structural XAML accessibility checks. Onboarding behavior and rollback are covered by
/// OnboardingStateTests; these tests avoid coupling to code-behind source text.</summary>
public sealed class OnboardingAccessibilityTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void Window_has_approved_size_and_constrained_layout()
    {
        var root = Document().Root!;
        Assert.Equal("640", (string?)root.Attribute("Width"));
        Assert.Equal("480", (string?)root.Attribute("Height"));

        var scroll = Descendants("ScrollViewer").Single();
        Assert.Equal("Auto", (string?)scroll.Attribute("VerticalScrollBarVisibility"));
        Assert.Equal("Disabled", (string?)scroll.Attribute("HorizontalScrollBarVisibility"));
    }

    [Fact]
    public void Window_exposes_the_approved_quick_start_copy()
    {
        var copy = Document().Descendants()
            .SelectMany(element => element.Attributes()
                .Where(attribute => attribute.Name.LocalName is "Text" or "Content")
                .Select(attribute => attribute.Value))
            .ToArray();
        foreach (var expected in new[]
        {
            "Ready in under a minute.", "Quick setup", "Default targets",
            "Downloads, Documents, Desktop, Pictures", "Open anywhere", "Ctrl+Alt+Space",
            "Explorer SendTo shortcut", "Start with Windows", "_Decide later", "_Open wheel",
            "_Finish setup",
        })
            Assert.Contains(expected, copy);
    }

    [Fact]
    public void Interactive_controls_expose_keyboard_and_automation_metadata()
    {
        var elements = Document().Descendants().ToArray();
        var sendTo = elements.Single(element =>
            (string?)element.Attribute("AutomationProperties.Name") == "Enable Explorer SendTo shortcut");
        var startup = elements.Single(element =>
            (string?)element.Attribute("AutomationProperties.Name") == "Start Dropwheel with Windows");

        Assert.Equal("Adds Dropwheel to Explorer's Send to menu",
            (string?)sendTo.Attribute("AutomationProperties.HelpText"));
        Assert.Equal("Launches Dropwheel after sign-in",
            (string?)startup.Attribute("AutomationProperties.HelpText"));
        Assert.Contains(elements, element => (string?)element.Attribute("KeyboardNavigation.TabIndex") == "0");
        Assert.Contains(elements, element => (string?)element.Attribute("KeyboardNavigation.TabIndex") == "1");
    }

    [Fact]
    public void Inline_error_is_focusable_and_assertive()
    {
        var error = Document().Descendants().Single(element =>
            (string?)element.Attribute(Xaml + "Name") == "ErrorText");

        Assert.Equal("True", (string?)error.Attribute("Focusable"));
        Assert.Equal("Assertive", (string?)error.Attribute("AutomationProperties.LiveSetting"));
    }

    private static IEnumerable<XElement> Descendants(string localName) =>
        Document().Descendants().Where(element => element.Name.LocalName == localName);

    private static XDocument Document() => XDocument.Load(Path.Combine(
        RepositoryRoot(), "src", "Dropwheel", "UI", "OnboardingWindow.xaml"));

    private static string RepositoryRoot()
    {
        var injectedRoot = typeof(OnboardingAccessibilityTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "RepositoryRoot")?.Value;
        foreach (var start in new[]
        {
            injectedRoot,
            Environment.GetEnvironmentVariable("DROPWHEEL_REPOSITORY_ROOT"),
            AppContext.BaseDirectory,
            Environment.CurrentDirectory,
        }.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            for (var directory = new DirectoryInfo(start!); directory != null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "Dropwheel.slnx"))) return directory.FullName;
        }
        throw new DirectoryNotFoundException("Could not locate the Dropwheel repository root.");
    }
}
