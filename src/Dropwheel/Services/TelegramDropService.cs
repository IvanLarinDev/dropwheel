using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using Dropwheel.Models;

namespace Dropwheel.Services;

public enum TelegramDropKind { Files, Text }

public sealed class TelegramDropResult
{
    public TelegramDropKind Kind { get; init; }
    public int Count { get; init; }
}

internal sealed class TelegramClipboardPayload
{
    public required TelegramDropKind Kind { get; init; }
    public string[] Files { get; init; } = Array.Empty<string>();
    public string? Text { get; init; }

    public int Count => Kind == TelegramDropKind.Files ? Files.Length : 1;

    public void Copy()
    {
        if (Kind == TelegramDropKind.Files)
        {
            var list = new StringCollection();
            list.AddRange(Files);
            SetClipboard(() => Clipboard.SetFileDropList(list));
            return;
        }

        SetClipboard(() => Clipboard.SetText(Text ?? "", TextDataFormat.UnicodeText));
    }

    private static void SetClipboard(Action set)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                set();
                return;
            }
            catch (ExternalException) when (attempt < 2)
            {
                Thread.Sleep(50);
            }
        }
    }
}

public static class TelegramDropService
{
    private static readonly TimeSpan PasteTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan PastePoll = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan PasteReadyDelay = TimeSpan.FromMilliseconds(650);

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int processId);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

    public static bool IsTelegramTarget(TargetItem target)
    {
        if (target.IsGroup || !Uri.TryCreate(target.Path, UriKind.Absolute, out var uri))
            return false;

        if (uri.Scheme.Equals("tg", StringComparison.OrdinalIgnoreCase))
            return true;

        if (!uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return false;

        return uri.Host.Equals("t.me", StringComparison.OrdinalIgnoreCase)
               || uri.Host.Equals("telegram.me", StringComparison.OrdinalIgnoreCase)
               || uri.Host.Equals("telegram.dog", StringComparison.OrdinalIgnoreCase)
               || uri.Host.EndsWith(".t.me", StringComparison.OrdinalIgnoreCase);
    }

    public static bool CanAccept(TargetItem target, IDataObject data) =>
        IsTelegramTarget(target) && HasSendablePayload(data);

    public static string LaunchPathFor(TargetItem target) =>
        IsTelegramTarget(target) && LinkTargetService.CreateTarget(target.Path) is { } linkTarget
            ? linkTarget.Path
            : target.Path;

    public static void PasteIntoTelegramWhenReady(Action<bool>? completed = null)
    {
        _ = Task.Run(async () =>
        {
            bool pasted = false;
            try
            {
                pasted = await PasteIntoTelegramWhenReady(
                    PasteTimeout,
                    PastePoll,
                    foregroundTelegramWindow: ForegroundTelegramWindow,
                    activateTelegramWindow: TryActivateTelegramWindow);
            }
            catch (Exception ex)
            {
                ErrorLog.Write("Could not paste Telegram drop payload", ex);
            }

            completed?.Invoke(pasted);
        });
    }

    public static TelegramDropResult? CopyToClipboard(IDataObject data, string stagingFolder)
    {
        var payload = CreatePayload(data, stagingFolder);
        if (payload == null) return null;

        payload.Copy();
        return new TelegramDropResult { Kind = payload.Kind, Count = payload.Count };
    }

    public static TelegramDropResult? CopyFilesToClipboard(IEnumerable<string> paths)
    {
        var files = paths.Where(path => File.Exists(path) || Directory.Exists(path)).ToArray();
        if (files.Length == 0) return null;
        var payload = new TelegramClipboardPayload { Kind = TelegramDropKind.Files, Files = files };
        payload.Copy();
        return new TelegramDropResult { Kind = TelegramDropKind.Files, Count = files.Length };
    }

    private static bool HasSendablePayload(IDataObject data) =>
        RealFiles(data).Length > 0
        || VirtualFileService.HasVirtualFiles(data)
        || TextDropService.HasPotentialText(data);

    internal static TelegramClipboardPayload? CreatePayload(IDataObject data, string stagingFolder)
    {
        var realFiles = RealFiles(data);
        if (realFiles.Length > 0)
            return new TelegramClipboardPayload { Kind = TelegramDropKind.Files, Files = realFiles };

        if (VirtualFileService.HasVirtualFiles(data))
        {
            Directory.CreateDirectory(stagingFolder);
            var saved = VirtualFileService.Extract(data, stagingFolder);
            if (saved.Length > 0)
                return new TelegramClipboardPayload { Kind = TelegramDropKind.Files, Files = saved };
        }

        var text = TextDropService.GetText(data);
        return string.IsNullOrEmpty(text)
            ? null
            : new TelegramClipboardPayload { Kind = TelegramDropKind.Text, Text = text };
    }

    internal static async Task<bool> PasteIntoTelegramWhenReady(
        TimeSpan timeout,
        TimeSpan poll,
        Action? paste = null,
        Func<IntPtr>? foregroundTelegramWindow = null,
        Func<IntPtr>? activateTelegramWindow = null,
        TimeSpan? readyDelay = null)
    {
        var foreground = foregroundTelegramWindow ?? ForegroundTelegramWindow;
        var deadline = DateTime.UtcNow + timeout;
        do
        {
            var expectedWindow = foreground();
            if (expectedWindow != IntPtr.Zero)
            {
                return await PasteAfterReadyDelay(expectedWindow, foreground, paste, readyDelay);
            }

            var activatedWindow = activateTelegramWindow?.Invoke() ?? IntPtr.Zero;
            if (activatedWindow != IntPtr.Zero)
            {
                return await PasteAfterReadyDelay(activatedWindow, foreground, paste, readyDelay);
            }

            await Task.Delay(poll);
        }
        while (DateTime.UtcNow <= deadline);

        return false;
    }

    private static async Task<bool> PasteAfterReadyDelay(
        IntPtr expectedWindow,
        Func<IntPtr> foregroundTelegramWindow,
        Action? paste,
        TimeSpan? readyDelay)
    {
        await Task.Delay(readyDelay ?? PasteReadyDelay);
        if (foregroundTelegramWindow() != expectedWindow) return false;
        if (paste != null) paste();
        else await Application.Current.Dispatcher.InvokeAsync(() => System.Windows.Forms.SendKeys.SendWait("^v"));
        return true;
    }

    internal static bool IsTelegramProcessName(string? processName) =>
        processName != null
        && (processName.Equals("Telegram", StringComparison.OrdinalIgnoreCase)
            || processName.Equals("TelegramDesktop", StringComparison.OrdinalIgnoreCase));

    private static IntPtr ForegroundTelegramWindow()
    {
        var hWnd = GetForegroundWindow();
        if (hWnd == IntPtr.Zero) return IntPtr.Zero;

        var threadId = GetWindowThreadProcessId(hWnd, out var processId);
        if (threadId == 0 || processId == 0) return IntPtr.Zero;

        try
        {
            using var process = Process.GetProcessById(processId);
            return IsTelegramProcessName(process.ProcessName) ? hWnd : IntPtr.Zero;
        }
        catch { return IntPtr.Zero; }
    }

    private static IntPtr TryActivateTelegramWindow()
    {
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (!IsTelegramProcessName(process.ProcessName)) continue;
                if (process.MainWindowHandle == IntPtr.Zero) continue;
                if (SetForegroundWindow(process.MainWindowHandle)) return process.MainWindowHandle;
            }
            catch
            {
                // Processes can exit while being inspected; try the next one.
            }
            finally
            {
                process.Dispose();
            }
        }

        return IntPtr.Zero;
    }

    private static string[] RealFiles(IDataObject data) =>
        data.GetData(DataFormats.FileDrop) is string[] files
            ? files.Where(path => File.Exists(path) || Directory.Exists(path)).ToArray()
            : Array.Empty<string>();
}
