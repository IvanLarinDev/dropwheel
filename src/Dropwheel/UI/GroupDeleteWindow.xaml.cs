using System.Windows;

namespace Dropwheel.UI;

/// <summary>What the user chose to do with a group being deleted.</summary>
public enum GroupDeleteChoice { Cancel, KeepChildren, DeleteAll }

/// <summary>Confirmation shown before a group with children is deleted, offering a non-destructive
/// path: keep the targets (move them out to the main wheel) or delete the group and everything in it.</summary>
public partial class GroupDeleteWindow : Window
{
    public GroupDeleteChoice Choice { get; private set; } = GroupDeleteChoice.Cancel;

    public GroupDeleteWindow(string groupName, int childCount)
    {
        InitializeComponent();
        Themes.ApplyWindow(this);
        Caption.Text = $"The “{groupName}” group holds {childCount} targets. Choose what happens to them:";
        KeepText.Text = "Keep the targets, delete only the group";
        KeepSub.Text = $"The {childCount} targets move out to the main wheel";
        DeleteText.Text = $"Delete the group and all {childCount} targets";
    }

    private void OnContinue(object sender, RoutedEventArgs e)
    {
        Choice = KeepOption.IsChecked == true ? GroupDeleteChoice.KeepChildren : GroupDeleteChoice.DeleteAll;
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
