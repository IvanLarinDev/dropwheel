using System.IO;
using Dropwheel.Services;

namespace Dropwheel.Tests;

public sealed class DropHistoryServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dw_history_" + Guid.NewGuid().ToString("N"));

    public DropHistoryServiceTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (DirectoryNotFoundException) { }
    }

    [Fact]
    public void Append_writes_newest_entries_first_and_respects_limit()
    {
        var path = Path.Combine(_root, "drop-history.json");

        DropHistoryService.Append(Entry("Downloads", 1), path, limit: 2);
        DropHistoryService.Append(Entry("Documents", 2), path, limit: 2);
        DropHistoryService.Append(Entry("Pictures", 3), path, limit: 2);

        var entries = DropHistoryService.Load(path);

        Assert.Equal(2, entries.Count);
        Assert.Equal("Pictures", entries[0].TargetName);
        Assert.Equal("Documents", entries[1].TargetName);
    }

    [Fact]
    public void Load_returns_empty_for_missing_or_corrupt_history()
    {
        var path = Path.Combine(_root, "drop-history.json");

        Assert.Empty(DropHistoryService.Load(path));

        File.WriteAllText(path, "{not json");

        Assert.Empty(DropHistoryService.Load(path));
    }

    [Fact]
    public void Append_recovers_from_corrupt_history()
    {
        var path = Path.Combine(_root, "drop-history.json");
        File.WriteAllText(path, "{not json");

        DropHistoryService.Append(Entry("Inbox", 4), path, limit: 5);

        var entry = Assert.Single(DropHistoryService.Load(path));
        Assert.Equal("Inbox", entry.TargetName);
        Assert.Equal(DropHistoryAction.Copy, entry.Action);
    }

    [Fact]
    public void Clear_removes_entries_and_leaves_readable_history_file()
    {
        var path = Path.Combine(_root, "drop-history.json");
        DropHistoryService.Append(Entry("Inbox", 1), path, limit: 5);

        DropHistoryService.Clear(path);

        Assert.Empty(DropHistoryService.Load(path));
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void MenuSummary_describes_action_count_target_and_failed_status()
    {
        var entry = Entry("Telegram", 2);
        entry = new DropHistoryEntry
        {
            AtUtc = entry.AtUtc,
            Action = DropHistoryAction.Telegram,
            Payload = DropPayloadKind.Files,
            Status = DropHistoryStatus.Failed,
            TargetName = entry.TargetName,
            TargetPath = entry.TargetPath,
            ItemCount = entry.ItemCount,
        };

        var summary = DropHistoryService.MenuSummary(entry);

        Assert.Contains("Telegram", summary);
        Assert.Contains("2 files", summary);
        Assert.Contains("Failed", summary);
    }

    [Fact]
    public void MenuSummary_uses_target_word_for_added_targets()
    {
        var entry = new DropHistoryEntry
        {
            AtUtc = DateTimeOffset.UnixEpoch,
            Action = DropHistoryAction.AddTargets,
            Payload = DropPayloadKind.Files,
            Status = DropHistoryStatus.Succeeded,
            TargetName = "Wheel",
            TargetPath = "",
            ItemCount = 1,
        };

        var summary = DropHistoryService.MenuSummary(entry);

        Assert.Contains("Added 1 target", summary);
        Assert.DoesNotContain("Succeeded", summary);
    }

    [Fact]
    public void MenuToolTip_includes_open_folder_and_detail()
    {
        var destination = Directory.CreateDirectory(Path.Combine(_root, "destination"));
        var entry = new DropHistoryEntry
        {
            AtUtc = DateTimeOffset.UnixEpoch,
            Action = DropHistoryAction.Copy,
            Payload = DropPayloadKind.Files,
            Status = DropHistoryStatus.Succeeded,
            TargetName = "Pictures",
            TargetPath = destination.FullName,
            Destination = destination.FullName,
            ItemCount = 1,
            Detail = "Dropped from Explorer SendTo.",
        };

        var tooltip = DropHistoryService.MenuToolTip(entry);

        Assert.Contains($"Open: {destination.FullName}", tooltip);
        Assert.Contains("Detail: Dropped from Explorer SendTo.", tooltip);
    }

    [Fact]
    public void MenuToolTip_says_reveal_for_file_destination()
    {
        var folder = Directory.CreateDirectory(Path.Combine(_root, "saved"));
        var file = Path.Combine(folder.FullName, "text.txt");
        File.WriteAllText(file, "saved text");
        var entry = new DropHistoryEntry
        {
            AtUtc = DateTimeOffset.UnixEpoch,
            Action = DropHistoryAction.SaveText,
            Payload = DropPayloadKind.Text,
            Status = DropHistoryStatus.Succeeded,
            TargetName = "Documents",
            TargetPath = folder.FullName,
            Destination = file,
            ItemCount = 1,
        };

        var tooltip = DropHistoryService.MenuToolTip(entry);

        Assert.Contains($"Reveal: {file}", tooltip);
    }

    [Fact]
    public void MenuToolTip_keeps_non_file_uri_target_visible()
    {
        var entry = new DropHistoryEntry
        {
            AtUtc = DateTimeOffset.UnixEpoch,
            Action = DropHistoryAction.Telegram,
            Payload = DropPayloadKind.Files,
            Status = DropHistoryStatus.Succeeded,
            TargetName = "Telegram",
            TargetPath = "tg://privatepost?channel=1&post=1",
            ItemCount = 1,
        };

        Assert.Equal("Target: tg://privatepost?channel=1&post=1", DropHistoryService.MenuToolTip(entry));
    }

    [Fact]
    public void DestinationFolder_prefers_existing_destination()
    {
        var target = Directory.CreateDirectory(Path.Combine(_root, "target"));
        var destination = Directory.CreateDirectory(Path.Combine(_root, "destination"));
        var entry = new DropHistoryEntry
        {
            AtUtc = DateTimeOffset.UnixEpoch,
            Action = DropHistoryAction.Copy,
            Payload = DropPayloadKind.Files,
            Status = DropHistoryStatus.Succeeded,
            TargetName = "Pictures",
            TargetPath = target.FullName,
            Destination = destination.FullName,
            ItemCount = 1,
        };

        Assert.Equal(destination.FullName, DropHistoryService.DestinationFolder(entry));
    }

    [Fact]
    public void DestinationFolder_uses_parent_for_file_target()
    {
        var folder = Directory.CreateDirectory(Path.Combine(_root, "app"));
        var file = Path.Combine(folder.FullName, "tool.exe");
        File.WriteAllText(file, "");
        var entry = new DropHistoryEntry
        {
            AtUtc = DateTimeOffset.UnixEpoch,
            Action = DropHistoryAction.Run,
            Payload = DropPayloadKind.Files,
            Status = DropHistoryStatus.Succeeded,
            TargetName = "Tool",
            TargetPath = file,
            ItemCount = 1,
        };

        Assert.Equal(folder.FullName, DropHistoryService.DestinationFolder(entry));
    }

    [Fact]
    public void DestinationLocation_selects_existing_file_destination()
    {
        var folder = Directory.CreateDirectory(Path.Combine(_root, "saved"));
        var file = Path.Combine(folder.FullName, "note.txt");
        File.WriteAllText(file, "hello");
        var entry = new DropHistoryEntry
        {
            AtUtc = DateTimeOffset.UnixEpoch,
            Action = DropHistoryAction.SaveText,
            Payload = DropPayloadKind.Text,
            Status = DropHistoryStatus.Succeeded,
            TargetName = "Documents",
            TargetPath = folder.FullName,
            Destination = file,
            ItemCount = 1,
        };

        var location = DropHistoryService.DestinationLocation(entry);

        Assert.Equal(file, location?.Path);
        Assert.True(location?.SelectFile);
        Assert.Equal(folder.FullName, DropHistoryService.DestinationFolder(entry));
    }

    [Fact]
    public void DestinationFolder_ignores_non_file_uri_targets()
    {
        var entry = new DropHistoryEntry
        {
            AtUtc = DateTimeOffset.UnixEpoch,
            Action = DropHistoryAction.Telegram,
            Payload = DropPayloadKind.Files,
            Status = DropHistoryStatus.Succeeded,
            TargetName = "Telegram",
            TargetPath = "tg://privatepost?channel=1&post=1",
            ItemCount = 1,
        };

        Assert.Null(DropHistoryService.DestinationFolder(entry));
    }

    [Fact]
    public void ClipboardText_is_one_line_per_drop_with_the_destination()
    {
        var withDest = Entry("Video", 3);
        var noDest = new DropHistoryEntry
        {
            AtUtc = DateTimeOffset.UnixEpoch.AddMinutes(1),
            Action = DropHistoryAction.Copy,
            Payload = DropPayloadKind.Files,
            Status = DropHistoryStatus.Succeeded,
            TargetName = "Notes",
            TargetPath = @"C:\Target",
            ItemCount = 1,
        };

        var text = DropHistoryService.ClipboardText(new[] { withDest, noDest });

        var lines = text.Split(Environment.NewLine);
        Assert.Equal(2, lines.Length);
        Assert.EndsWith(@"— C:\Target", lines[0]);
        Assert.Contains("-> Video", lines[0]);
        Assert.DoesNotContain("—", lines[1]);
    }

    private static DropHistoryEntry Entry(string targetName, int count) => new()
    {
        AtUtc = DateTimeOffset.UnixEpoch.AddMinutes(count),
        Action = DropHistoryAction.Copy,
        Payload = DropPayloadKind.Files,
        Status = DropHistoryStatus.Succeeded,
        TargetName = targetName,
        TargetPath = @"C:\Target",
        Destination = @"C:\Target",
        ItemCount = count,
    };
}
