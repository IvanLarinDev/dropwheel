using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace Dropwheel.Services;

/// <summary>Finds the folder, app or file sitting under the cursor in File Explorer or on the
/// desktop, so an Alt+Shift orb drag can pin it. The folder path comes from Explorer's own shell
/// object (reliable) and the item name from UI Automation (the only way to hit-test an arbitrary
/// point in another app). Anything outside Explorer and the desktop returns null rather than
/// guessing — that limitation is by design.</summary>
public static class CursorTargetLocator
{
    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(POINT p);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, char[] buffer, int max);

    private const uint GA_ROOT = 2;

    /// <summary>Resolves an existing filesystem path under the current cursor position, or null.
    /// <paramref name="isOwnWindow"/> lets the caller skip its own overlay so the orb sitting over
    /// the target does not shadow it.</summary>
    public static string? ResolveUnderCursor(Func<IntPtr, bool> isOwnWindow)
    {
        if (!GetCursorPos(out var p)) return null;

        var hit = WindowFromPoint(p);
        if (hit == IntPtr.Zero) return null;
        var root = GetAncestor(hit, GA_ROOT);
        if (root == IntPtr.Zero || isOwnWindow(root)) return null;

        var folder = FolderForWindow(root);
        if (folder == null) return null;

        var name = ItemNameUnderCursor(p.X, p.Y);
        return ResolveInFolder(folder, name);
    }

    /// <summary>Turns a folder path and a possibly extension-less display name into a real path.
    /// A null or empty name resolves to the folder itself (dropping on empty space in a folder
    /// pins that folder). When Explorer hides extensions the name won't match on disk, so the
    /// folder is scanned for an entry whose name-without-extension equals the display name.</summary>
    internal static string? ResolveInFolder(string folder, string? name)
    {
        if (string.IsNullOrWhiteSpace(folder)) return null;
        if (string.IsNullOrWhiteSpace(name))
            return Directory.Exists(folder) ? folder : null;

        var direct = Path.Combine(folder, name);
        if (File.Exists(direct) || Directory.Exists(direct)) return direct;
        if (!Directory.Exists(folder)) return null;

        foreach (var entry in Directory.EnumerateFileSystemEntries(folder))
            if (string.Equals(Path.GetFileNameWithoutExtension(entry), name, StringComparison.OrdinalIgnoreCase))
                return entry;

        return null;
    }

    /// <summary>Cheap check for the drag: is the cursor over an Explorer window or the desktop that
    /// isn't our own overlay? Used to light the pin ring without the cost of a full shell/UIA probe
    /// on every frame.</summary>
    public static bool LooksLikeTargetWindow(Func<IntPtr, bool> isOwnWindow)
    {
        if (!GetCursorPos(out var p)) return false;
        var hit = WindowFromPoint(p);
        if (hit == IntPtr.Zero) return false;
        var root = GetAncestor(hit, GA_ROOT);
        if (root == IntPtr.Zero || isOwnWindow(root)) return false;
        return ClassName(root) is "Progman" or "WorkerW" or "CabinetWClass";
    }

    private static string? FolderForWindow(IntPtr root)
    {
        var cls = ClassName(root);
        if (cls is "Progman" or "WorkerW") return DesktopFolder();
        if (cls == "CabinetWClass") return ExplorerFolder(root);
        return null;
    }

    private static string DesktopFolder() => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

    /// <summary>Walks Explorer's own shell windows to read the folder path of the window under the
    /// cursor. Uses late-bound COM so no interop assembly is needed.</summary>
    private static string? ExplorerFolder(IntPtr root)
    {
        var shellType = Type.GetTypeFromProgID("Shell.Application");
        if (shellType == null) return null;

        object? shell = null;
        try
        {
            shell = Activator.CreateInstance(shellType);
            dynamic windows = ((dynamic)shell!).Windows();
            int count = windows.Count;
            for (int i = 0; i < count; i++)
            {
                dynamic? window = windows.Item(i);
                if (window == null) continue;
                if (new IntPtr((long)window.HWND) != root) continue;
                string path = window.Document.Folder.Self.Path;
                return string.IsNullOrWhiteSpace(path) ? null : path;
            }
        }
        catch (Exception e) when (e is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
            return null;
        }
        finally
        {
            if (shell != null) Marshal.ReleaseComObject(shell);
        }
        return null;
    }

    private static string? ItemNameUnderCursor(int x, int y)
    {
        try
        {
            var element = AutomationElement.FromPoint(new System.Windows.Point(x, y));
            var name = element?.Current.Name;
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }
        catch (Exception e) when (e is ElementNotAvailableException or COMException or TimeoutException)
        {
            return null;
        }
    }

    private static string ClassName(IntPtr hwnd)
    {
        var buffer = new char[256];
        int length = GetClassName(hwnd, buffer, buffer.Length);
        return new string(buffer, 0, length);
    }
}
