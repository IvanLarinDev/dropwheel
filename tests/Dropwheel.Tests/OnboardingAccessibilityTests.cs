using System.IO;
using System.Reflection;

namespace Dropwheel.Tests;

public sealed class OnboardingAccessibilityTests
{
    [Fact]
    public void Onboarding_window_uses_approved_logical_size()
    {
        var path = OnboardingXamlPath();

        Assert.True(File.Exists(path), "The approved onboarding window must exist.");
        var xaml = File.ReadAllText(path);
        Assert.Contains("Width=\"640\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Height=\"480\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Onboarding_window_exposes_the_approved_quick_start_choices()
    {
        var xaml = File.ReadAllText(OnboardingXamlPath());
        var expectedText = new[]
        {
            "Ready in under a minute.",
            "Quick setup",
            "Default targets",
            "Downloads, Documents, Desktop, Pictures",
            "Open anywhere",
            "Ctrl+Alt+Space",
            "Explorer SendTo shortcut",
            "Start with Windows",
            "Decide later",
            "Open wheel",
            "Finish setup",
        };

        Assert.All(expectedText, text => Assert.Contains(text, xaml, StringComparison.Ordinal));
    }

    [Fact]
    public void Interactive_controls_have_keyboard_and_automation_contracts()
    {
        var xaml = File.ReadAllText(OnboardingXamlPath());
        var expectedContracts = new[]
        {
            "KeyboardNavigation.TabNavigation=\"Cycle\"",
            "AutomationProperties.LiveSetting=\"Polite\"",
            "AutomationProperties.Name=\"Enable Explorer SendTo shortcut\"",
            "AutomationProperties.HelpText=\"Adds Dropwheel to Explorer's Send to menu\"",
            "AutomationProperties.Name=\"Start Dropwheel with Windows\"",
            "AutomationProperties.HelpText=\"Launches Dropwheel after sign-in\"",
            "KeyboardNavigation.TabIndex=\"0\"",
            "KeyboardNavigation.TabIndex=\"1\"",
            "Content=\"_Decide later\"",
            "Content=\"_Open wheel\"",
            "Content=\"_Finish setup\"",
        };

        Assert.All(expectedContracts, contract => Assert.Contains(contract, xaml, StringComparison.Ordinal));
    }

    [Fact]
    public void Constrained_work_areas_remain_scrollable()
    {
        var xaml = File.ReadAllText(OnboardingXamlPath());
        var code = File.ReadAllText(Path.ChangeExtension(OnboardingXamlPath(), ".xaml.cs"));

        Assert.Contains("VerticalScrollBarVisibility=\"Auto\"", xaml, StringComparison.Ordinal);
        Assert.Contains("HorizontalScrollBarVisibility=\"Disabled\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MaxHeight = SystemParameters.WorkArea.Height", code, StringComparison.Ordinal);
        Assert.Contains("MaxWidth = SystemParameters.WorkArea.Width", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Inline_errors_receive_focus_and_raise_an_assertive_live_event()
    {
        var xaml = File.ReadAllText(OnboardingXamlPath());
        var code = File.ReadAllText(Path.ChangeExtension(OnboardingXamlPath(), ".xaml.cs"));

        Assert.Contains("x:Name=\"ErrorText\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Focusable=\"True\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.LiveSetting=\"Assertive\"", xaml, StringComparison.Ordinal);
        Assert.Contains("RaiseAutomationEvent(AutomationEvents.LiveRegionChanged)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_actions_route_through_the_tested_setup_coordinator()
    {
        var codePath = Path.ChangeExtension(OnboardingXamlPath(), ".xaml.cs");

        Assert.True(File.Exists(codePath), "Onboarding actions need a thin code-behind adapter.");
        var code = File.ReadAllText(codePath);
        var expectedWiring = new[]
        {
            "Themes.ApplyWindow(this)",
            "_setup.DecideLater()",
            "_setup.Finish(new OnboardingOptions(",
            "ExplorerSendToToggle.IsChecked == true",
            "StartWithWindowsToggle.IsChecked == true",
            "_openWheel()",
            "ErrorText.Visibility = Visibility.Visible",
        };

        Assert.All(expectedWiring, wiring => Assert.Contains(wiring, code, StringComparison.Ordinal));
        Assert.DoesNotContain("StartupService.", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ExplorerBridgeService.", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Open_wheel_callback_runs_only_after_setup_succeeds()
    {
        var codePath = Path.ChangeExtension(OnboardingXamlPath(), ".xaml.cs");
        var code = File.ReadAllText(codePath);

        Assert.Contains("if (TryFinish()) _openWheel();", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Application_startup_schedules_onboarding_only_after_successful_normal_startup()
    {
        var appCode = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "src", "Dropwheel", "App.xaml.cs"));
        const string gate = "OnboardingState.ShouldShow(TargetStore.Config, _exitAfterExplorerDelivery)";
        const string schedule = "Dispatcher.BeginInvoke(ShowOnboarding)";
        var requiredWiring = new[]
        {
            gate,
            schedule,
            "ExplorerBridgeService.InstallSendTo(CurrentAppPath())",
            "StartupService.SetEnabled(true)",
            "TargetStore.Save",
            "new OnboardingWindow(setup, () => _overlay?.ToggleCloud())",
        };

        Assert.All(requiredWiring, wiring => Assert.Contains(wiring, appCode, StringComparison.Ordinal));
        Assert.True(
            appCode.IndexOf("_watcher.Start();", StringComparison.Ordinal)
                < appCode.IndexOf(gate, StringComparison.Ordinal),
            "Onboarding must be scheduled only after normal startup has completed successfully.");
        Assert.DoesNotContain("OnboardingState.Complete", appCode, StringComparison.Ordinal);
    }

    [Fact]
    public void Application_wiring_restores_preexisting_system_integration_state_on_failure()
    {
        var appCode = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "src", "Dropwheel", "App.xaml.cs"));
        var rollbackWiring = new[]
        {
            "bool hadExplorerSendTo = ExplorerBridgeService.IsSendToInstalled()",
            "bool hadStartup = StartupService.IsEnabled",
            "rollbackExplorerSendTo:",
            "ExplorerBridgeService.UninstallSendTo()",
            "rollbackStartup:",
            "StartupService.SetEnabled(false)",
        };

        Assert.All(rollbackWiring, wiring => Assert.Contains(wiring, appCode, StringComparison.Ordinal));
    }

    [Fact]
    public void Dispatched_onboarding_failure_does_not_terminate_successful_startup()
    {
        var appCode = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "src", "Dropwheel", "App.xaml.cs"));

        var normalizedAppCode = appCode.ReplaceLineEndings("\n");
        Assert.Contains("private void ShowOnboarding()\n    {\n        try", normalizedAppCode, StringComparison.Ordinal);
        Assert.Contains("ErrorLog.Write(\"Could not show onboarding\", ex)", appCode, StringComparison.Ordinal);
    }

    [Fact]
    public void Tray_menu_refreshes_startup_state_after_onboarding_changes_it()
    {
        var trayCode = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "src", "Dropwheel", "App.Tray.cs"));
        int opening = trayCode.IndexOf("menu.Opening +=", StringComparison.Ordinal);
        Assert.True(opening >= 0, "The tray menu opening handler must exist.");

        int refresh = trayCode.IndexOf("auto.Checked = StartupService.IsEnabled;", opening, StringComparison.Ordinal);
        Assert.True(refresh > opening, "Opening the tray must re-read the current startup integration state.");
    }

    private static string OnboardingXamlPath() => Path.Combine(
        RepositoryRoot(), "src", "Dropwheel", "UI", "OnboardingWindow.xaml");

    private static string RepositoryRoot()
    {
        var injectedRoot = typeof(OnboardingAccessibilityTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(static attribute => attribute.Key == "RepositoryRoot")
            ?.Value;
        var starts = new[]
        {
            injectedRoot,
            Environment.GetEnvironmentVariable("DROPWHEEL_REPOSITORY_ROOT"),
            AppContext.BaseDirectory,
            Environment.CurrentDirectory,
        };

        foreach (var start in starts.Where(static value => !string.IsNullOrWhiteSpace(value)))
        {
            for (var directory = new DirectoryInfo(start!);
                 directory is not null;
                 directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Dropwheel.slnx")))
                    return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the Dropwheel repository root.");
    }
}
