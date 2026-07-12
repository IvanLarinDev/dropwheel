using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Dropwheel.UI;

public partial class OverlayWindow
{
    /// <summary>"←" tile — return from a group to the root level.</summary>
    private FrameworkElement MakeBackBubble()
    {
        var th = Themes.Current;
        var sq = new Border
        {
            Width = 64,
            Height = 64,
            CornerRadius = new CornerRadius(17),
            Background = new SolidColorBrush(th.TileBg),
            BorderBrush = new SolidColorBrush(th.GroupBorder),
            BorderThickness = new Thickness(1.2),
            Child = new TextBlock
            {
                Text = "←",
                FontSize = 24,
                Foreground = new SolidColorBrush(th.Label),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        var confidence = MakeConfidenceOverlay();
        var top = new Grid { Width = 70, Height = 66 };
        top.Children.Add(sq);
        top.Children.Add(confidence.Ring);
        top.Children.Add(confidence.Chip);
        var panel = new StackPanel
        { Width = 76, AllowDrop = true, Background = Brushes.Transparent };
        panel.Children.Add(top);
        var label = MakeLabel("Back");
        panel.Children.Add(label);
        panel.MouseEnter += (_, _) => { sq.Background = new SolidColorBrush(th.TileHot); SetSpokeLit(panel, true); };
        panel.MouseLeave += (_, _) => { sq.Background = new SolidColorBrush(th.TileBg); SetSpokeLit(panel, false); };
        panel.MouseLeftButtonUp += (_, e) => { EnterGroup(null); e.Handled = true; };
        panel.DragOver += (_, e) =>
        {
            StartGroupHover(null, back: true);
            e.Effects = DragDropEffects.Scroll;
            ShowGeneralConfidence(
                panel,
                "Back",
                "Hold to return to the previous wheel level.",
                ConfidenceTone.Info,
                activeLabelText: "Back");
            e.Handled = true;
        };
        panel.DragLeave += (_, _) =>
        {
            ClearConfidenceTarget(panel);
            _groupHover?.Stop();
        };
        System.Windows.Automation.AutomationProperties.SetName(panel, "Back to previous level");
        RegisterConfidenceVisuals(
            panel,
            confidence.Ring,
            confidence.Chip,
            confidence.ChipText,
            label,
            "Back to previous level",
            () => EnterGroup(null),
            "Back to previous level. Press Enter to return.");
        return panel;
    }
}
