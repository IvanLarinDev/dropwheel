using System.Windows;

namespace Dropwheel.UI;

/// <summary>What the user chose to do with a group being deleted.</summary>
public enum GroupDeleteChoice { Cancel, KeepChildren, DeleteAll }

/// <summary>Confirmation shown before a group with children is deleted, offering a non-destructive
/// path: keep the targets (move them out to the main wheel) or delete the group and everything in it.
/// The Primary verb tracks the selected option so the button says exactly what it will do.</summary>
public partial class GroupDeleteWindow : Window
{
    public GroupDeleteChoice Choice { get; private set; } = GroupDeleteChoice.Cancel;

    public GroupDeleteWindow(string groupName, int childCount)
    {
        InitializeComponent();
        Themes.ApplyWindow(this);
        Shell.Title = $"Delete the “{groupName}” group?";
        Caption.Text = $"It holds {childCount} targets. Choose what happens to them:";
        KeepText.Text = "Keep the targets, delete only the group";
        KeepSub.Text = $"The {childCount} targets move out to the main wheel";
        DeleteText.Text = $"Delete the group and all {childCount} targets";
        UpdatePrimary();
        KeepOption.Checked += (_, _) => UpdatePrimary();
        DeleteOption.Checked += (_, _) => UpdatePrimary();
        Shell.PrimaryClick += OnPrimary;
    }

    private void UpdatePrimary() =>
        Shell.PrimaryText = KeepOption.IsChecked == true ? "Delete group, keep items" : "Delete everything";

    private void OnPrimary(object sender, RoutedEventArgs e)
    {
        Choice = KeepOption.IsChecked == true ? GroupDeleteChoice.KeepChildren : GroupDeleteChoice.DeleteAll;
        DialogResult = true;
    }
}
