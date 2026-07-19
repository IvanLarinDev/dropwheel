using System.Windows;
using System.Windows.Automation.Peers;
using Dropwheel.Services;

namespace Dropwheel.UI;

public partial class OnboardingWindow : Window
{
    private readonly OnboardingSetup _setup;
    private readonly Action _openWheel;

    internal OnboardingWindow(
        OnboardingSetup setup,
        OnboardingOptions initialOptions,
        Action openWheel)
    {
        _setup = setup;
        _openWheel = openWheel;
        InitializeComponent();
        ExplorerSendToToggle.IsChecked = initialOptions.EnableExplorerSendTo;
        StartWithWindowsToggle.IsChecked = initialOptions.StartWithWindows;
        Themes.ApplyWindow(this);
        MaxHeight = SystemParameters.WorkArea.Height;
        MaxWidth = SystemParameters.WorkArea.Width;
    }

    private void OnOptionChanged(object sender, RoutedEventArgs e)
    {
        int readyCount = 2
            + (ExplorerSendToToggle.IsChecked == true ? 1 : 0)
            + (StartWithWindowsToggle.IsChecked == true ? 1 : 0);
        ReadyCountText.Text = $"{readyCount} of 4 ready";
        System.Windows.Automation.AutomationProperties.SetName(
            ReadyCountText, $"Setup progress, {readyCount} of 4 ready");
    }

    private void OnDecideLater(object sender, RoutedEventArgs e)
    {
        try
        {
            _setup.DecideLater();
            Close();
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private void OnFinish(object sender, RoutedEventArgs e) => TryFinish();

    private void OnOpenWheel(object sender, RoutedEventArgs e)
    {
        if (TryFinish()) _openWheel();
    }

    private bool TryFinish()
    {
        try
        {
            _setup.Finish(new OnboardingOptions(
                ExplorerSendToToggle.IsChecked == true,
                StartWithWindowsToggle.IsChecked == true));
            Close();
            return true;
        }
        catch (Exception ex)
        {
            ShowError(ex);
            return false;
        }
    }

    private void ShowError(Exception ex)
    {
        ErrorLog.Write("Onboarding setup failed", ex);
        ErrorText.Visibility = Visibility.Visible;
        ErrorText.Focus();
        UIElementAutomationPeer.CreatePeerForElement(ErrorText)?
            .RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }
}
