using System.IO;
using System.Windows;
using Dropwheel.Models;
using Dropwheel.Services;
using WpfDataObject = System.Windows.DataObject;
using WpfDataFormats = System.Windows.DataFormats;

namespace Dropwheel.Tests;

public sealed class TelegramDropServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dw_tgdrop_" + Guid.NewGuid().ToString("N"));

    public TelegramDropServiceTests() => Directory.CreateDirectory(_root);
    public void Dispose() { try { Directory.Delete(_root, true); } catch (DirectoryNotFoundException) { } }

    [Theory]
    [InlineData("tg://privatepost?channel=4379453334&post=1", true)]
    [InlineData("tg://resolve?domain=telegram", true)]
    [InlineData("https://t.me/c/4379453334/1", true)]
    [InlineData("https://durov.t.me", true)]
    [InlineData("https://example.com/file", false)]
    [InlineData("C:\\Temp\\file.txt", false)]
    public void IsTelegramTarget_detects_telegram_uri_targets(string path, bool expected)
    {
        var target = new TargetItem { Name = "target", Path = path };

        Assert.Equal(expected, TelegramDropService.IsTelegramTarget(target));
    }

    [Fact]
    public void IsTelegramTarget_rejects_groups()
    {
        var target = new TargetItem
        {
            Name = "group",
            Path = "tg://resolve?domain=telegram",
            Children = new(),
        };

        Assert.False(TelegramDropService.IsTelegramTarget(target));
    }

    [Fact]
    public void CanAccept_requires_telegram_target_and_payload()
    {
        var data = new WpfDataObject();
        data.SetData(WpfDataFormats.UnicodeText, "hello");

        Assert.True(TelegramDropService.CanAccept(
            new TargetItem { Name = "topic", Path = "tg://privatepost?channel=4379453334&post=1" },
            data));
        Assert.False(TelegramDropService.CanAccept(
            new TargetItem { Name = "web", Path = "https://example.com" },
            data));
    }

    [Fact]
    public void LaunchPathFor_converts_web_topic_link_to_desktop_deep_link()
    {
        var target = new TargetItem { Name = "topic", Path = "https://t.me/c/4379453334/1" };

        Assert.Equal("tg://privatepost?channel=4379453334&post=1", TelegramDropService.LaunchPathFor(target));
    }

    [Theory]
    [InlineData("Telegram", true)]
    [InlineData("telegramdesktop", true)]
    [InlineData("Dropwheel", false)]
    [InlineData(null, false)]
    public void IsTelegramProcessName_matches_only_telegram_processes(string? processName, bool expected) =>
        Assert.Equal(expected, TelegramDropService.IsTelegramProcessName(processName));

    [Fact]
    public async Task PasteIntoTelegramWhenReady_does_not_paste_when_telegram_is_not_foreground()
    {
        var pasted = false;

        var result = await TelegramDropService.PasteIntoTelegramWhenReady(
            TimeSpan.Zero,
            TimeSpan.Zero,
            () => pasted = true,
            () => "Dropwheel",
            readyDelay: TimeSpan.Zero);

        Assert.False(result);
        Assert.False(pasted);
    }

    [Fact]
    public async Task PasteIntoTelegramWhenReady_pastes_when_telegram_is_foreground()
    {
        var pasted = false;

        var result = await TelegramDropService.PasteIntoTelegramWhenReady(
            TimeSpan.Zero,
            TimeSpan.Zero,
            () => pasted = true,
            () => "Telegram",
            readyDelay: TimeSpan.Zero);

        Assert.True(result);
        Assert.True(pasted);
    }

    [Fact]
    public async Task PasteIntoTelegramWhenReady_pastes_after_activating_telegram_window()
    {
        var pasted = false;
        var activated = false;

        var result = await TelegramDropService.PasteIntoTelegramWhenReady(
            TimeSpan.Zero,
            TimeSpan.Zero,
            () => pasted = true,
            () => "Dropwheel",
            () => activated = true,
            readyDelay: TimeSpan.Zero);

        Assert.True(result);
        Assert.True(activated);
        Assert.True(pasted);
    }

    [Fact]
    public void CreatePayload_prefers_file_drop_list_over_text()
    {
        var file = Path.Combine(_root, "note.txt");
        File.WriteAllText(file, "hello");
        var data = new WpfDataObject();
        data.SetData(WpfDataFormats.FileDrop, new[] { file });
        data.SetData(WpfDataFormats.UnicodeText, "fallback text");

        var payload = TelegramDropService.CreatePayload(data, Path.Combine(_root, "staging"));

        Assert.NotNull(payload);
        Assert.Equal(TelegramDropKind.Files, payload.Kind);
        Assert.Equal(new[] { file }, payload.Files);
    }

    [Fact]
    public void CreatePayload_uses_text_when_no_files_are_present()
    {
        var data = new WpfDataObject();
        data.SetData(WpfDataFormats.UnicodeText, "message");

        var payload = TelegramDropService.CreatePayload(data, Path.Combine(_root, "staging"));

        Assert.NotNull(payload);
        Assert.Equal(TelegramDropKind.Text, payload.Kind);
        Assert.Equal("message", payload.Text);
    }

    [Fact]
    public void CreatePayload_ignores_missing_file_paths()
    {
        var data = new WpfDataObject();
        data.SetData(WpfDataFormats.FileDrop, new[] { Path.Combine(_root, "missing.txt") });

        Assert.Null(TelegramDropService.CreatePayload(data, Path.Combine(_root, "staging")));
    }
}
