using Dropwheel.Models;
using Dropwheel.Services;

namespace Dropwheel.Tests;

public sealed class DropIntentTests
{
    [Fact]
    public void ClassifyTarget_prefers_group_and_telegram_identities()
    {
        Assert.Equal(DropTargetKind.Group,
            DropIntent.ClassifyTarget(
                new TargetItem { Name = "Tools", Children = new List<TargetItem>() },
                isFolderTarget: true,
                isRunTarget: true));

        Assert.Equal(DropTargetKind.Telegram,
            DropIntent.ClassifyTarget(
                new TargetItem { Name = "Chat", Path = "tg://resolve?domain=dropwheel" },
                isFolderTarget: false,
                isRunTarget: false));
    }

    [Fact]
    public void ClassifyTarget_uses_resolved_folder_and_run_flags()
    {
        var target = new TargetItem { Name = "Shortcut", Path = @"C:\missing-shortcut.lnk" };

        Assert.Equal(DropTargetKind.Run,
            DropIntent.ClassifyTarget(target, isFolderTarget: false, isRunTarget: true));
        Assert.Equal(DropTargetKind.Folder,
            DropIntent.ClassifyTarget(target, isFolderTarget: true, isRunTarget: false));
    }

    [Fact]
    public void ClassifyTarget_promotes_folder_sorters()
    {
        var target = new TargetItem
        {
            Name = "Sorter",
            Path = @"C:\missing-sorter-folder",
            Rules = new List<SortRule> { new() { Dest = "Images" } },
        };

        Assert.Equal(DropTargetKind.Sorter,
            DropIntent.ClassifyTarget(target, isFolderTarget: true, isRunTarget: false));
    }

    [Fact]
    public void ClassifyTarget_marks_missing_when_no_identity_matches()
    {
        Assert.Equal(DropTargetKind.Missing,
            DropIntent.ClassifyTarget(
                new TargetItem { Name = "Gone", Path = @"C:\definitely-missing-dropwheel-target" },
                isFolderTarget: false,
                isRunTarget: false));
    }

    [Fact]
    public void Compatibility_allows_files_for_folder_sorter_run_and_telegram()
    {
        Assert.True(DropIntent.Compatibility(DropPayloadKind.Files, DropTargetKind.Folder).CanReceive);
        Assert.Equal(DropCompatibilityLevel.Caution,
            DropIntent.Compatibility(DropPayloadKind.Files, DropTargetKind.Sorter).Level);
        Assert.Equal(DropCompatibilityLevel.Caution,
            DropIntent.Compatibility(DropPayloadKind.Files, DropTargetKind.Run).Level);
        Assert.Equal(DropCompatibilityLevel.Caution,
            DropIntent.Compatibility(DropPayloadKind.Files, DropTargetKind.Telegram).Level);
    }

    [Fact]
    public void Compatibility_blocks_non_files_for_run_targets()
    {
        Assert.False(DropIntent.Compatibility(DropPayloadKind.VirtualFiles, DropTargetKind.Run).CanReceive);
        Assert.False(DropIntent.Compatibility(DropPayloadKind.Link, DropTargetKind.Run).CanReceive);
        Assert.False(DropIntent.Compatibility(DropPayloadKind.Text, DropTargetKind.Run).CanReceive);
    }

    [Fact]
    public void Compatibility_allows_text_and_virtual_files_for_sorters()
    {
        Assert.Equal(DropCompatibilityLevel.Caution,
            DropIntent.Compatibility(DropPayloadKind.VirtualFiles, DropTargetKind.Sorter).Level);
        Assert.Equal(DropCompatibilityLevel.Caution,
            DropIntent.Compatibility(DropPayloadKind.Text, DropTargetKind.Sorter).Level);
    }

    [Fact]
    public void Compatibility_blocks_groups_missing_targets_and_unknown_payloads()
    {
        Assert.False(DropIntent.Compatibility(DropPayloadKind.Files, DropTargetKind.Group).CanReceive);
        Assert.False(DropIntent.Compatibility(DropPayloadKind.Files, DropTargetKind.Missing).CanReceive);
        Assert.False(DropIntent.Compatibility(DropPayloadKind.Unsupported, DropTargetKind.Folder).CanReceive);
    }

    [Fact]
    public void TrustGate_confirms_file_run_targets()
    {
        var preflight = DropTrustGate.Evaluate(
            new TargetItem { Name = "Scripts" },
            DropPayloadKind.Files,
            DropTargetKind.Run,
            itemCount: 2);

        Assert.NotNull(preflight);
        Assert.Equal("Run", preflight.Value.PrimaryText);
    }

    [Fact]
    public void TrustGate_does_not_confirm_non_file_payloads_for_run_targets()
    {
        Assert.Null(DropTrustGate.Evaluate(
            new TargetItem { Name = "Scripts" },
            DropPayloadKind.Text,
            DropTargetKind.Run,
            itemCount: 1));
    }

    [Fact]
    public void TrustGate_confirms_telegram_handoff()
    {
        var preflight = DropTrustGate.Evaluate(
            new TargetItem { Name = "Telegram" },
            DropPayloadKind.Text,
            DropTargetKind.Telegram,
            itemCount: 1);

        Assert.NotNull(preflight);
        Assert.Contains("clipboard", preflight.Value.Message);
    }

    [Fact]
    public void TrustGate_confirms_watched_sorters_only()
    {
        var watched = new TargetItem { Name = "Inbox", Watch = true };
        var manual = new TargetItem { Name = "Inbox" };

        Assert.NotNull(DropTrustGate.Evaluate(
            watched,
            DropPayloadKind.Files,
            DropTargetKind.Sorter,
            itemCount: 3));
        Assert.Null(DropTrustGate.Evaluate(
            manual,
            DropPayloadKind.Files,
            DropTargetKind.Sorter,
            itemCount: 3));
    }

    [Fact]
    public void TrustGate_does_not_confirm_plain_folder_drops()
    {
        Assert.Null(DropTrustGate.Evaluate(
            new TargetItem { Name = "Downloads" },
            DropPayloadKind.Files,
            DropTargetKind.Folder,
            itemCount: 1));
    }

    [Fact]
    public void TrustGate_bypasses_only_the_selected_drop_confirmations()
    {
        var runTarget = new TargetItem { Name = "Scripts" };
        var telegramTarget = new TargetItem { Name = "Telegram" };
        var sorterTarget = new TargetItem { Name = "Inbox", Watch = true };

        Assert.Null(DropTrustGate.Evaluate(
            runTarget, DropPayloadKind.Files, DropTargetKind.Run, 1,
            DropConfirmationKind.RunDroppedFiles));
        Assert.NotNull(DropTrustGate.Evaluate(
            runTarget, DropPayloadKind.Files, DropTargetKind.Run, 1,
            DropConfirmationKind.TelegramHandoff));
        Assert.Null(DropTrustGate.Evaluate(
            telegramTarget, DropPayloadKind.Text, DropTargetKind.Telegram, 1,
            DropConfirmationKind.TelegramHandoff));
        Assert.Null(DropTrustGate.Evaluate(
            sorterTarget, DropPayloadKind.Files, DropTargetKind.Sorter, 1,
            DropConfirmationKind.WatchedSorterRules));
    }
}
