using Dropwheel.UI;

namespace Dropwheel.Tests;

/// <summary>Verifies the suggestion-chip state resolution for the hotkey fields: the chip matching
/// the field's own combo reads as selected, a chip held by the other hotkey field reads as
/// conflicting (spelling differences don't matter), and everything else stays clickable.</summary>
public sealed class SettingsHotkeyChipTests
{
    [Fact]
    public void Chip_matching_own_value_is_selected()
    {
        Assert.Equal(SettingsWindow.HotkeyChipKind.Selected,
            SettingsWindow.HotkeyChipState("Ctrl+Alt+Space", "ctrl+alt+space", "Ctrl+Shift+D"));
    }

    [Fact]
    public void Chip_held_by_the_other_field_is_conflicting()
    {
        Assert.Equal(SettingsWindow.HotkeyChipKind.Conflicting,
            SettingsWindow.HotkeyChipState("Ctrl+Shift+D", "Ctrl+Alt+Space", "control+shift+d"));
    }

    [Fact]
    public void Own_value_wins_over_a_conflict_with_the_other_field()
    {
        Assert.Equal(SettingsWindow.HotkeyChipKind.Selected,
            SettingsWindow.HotkeyChipState("Ctrl+Alt+D", "Ctrl+Alt+D", "Ctrl+Alt+D"));
    }

    [Theory]
    [InlineData("Ctrl+Alt+F12", "Ctrl+Alt+Space", "Ctrl+Shift+D")]
    [InlineData("Ctrl+Alt+F12", "", "")]
    public void Free_chip_stays_normal(string combo, string current, string other)
    {
        Assert.Equal(SettingsWindow.HotkeyChipKind.Normal,
            SettingsWindow.HotkeyChipState(combo, current, other));
    }
}
