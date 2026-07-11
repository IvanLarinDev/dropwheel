using System.Runtime.InteropServices;
using System.Text;
using ComTypes = System.Runtime.InteropServices.ComTypes;

namespace Dropwheel.Services;

/// <summary>Resolves a Windows .lnk shortcut to the file/folder it points at,
/// so a dragged-in shortcut becomes a target for its destination, not the .lnk itself.</summary>
public static class ShortcutResolver
{
    /// <summary>If <paramref name="path"/> is a .lnk with a resolvable target, returns that
    /// target path; otherwise returns <paramref name="path"/> unchanged.</summary>
    public static string Resolve(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;
        if (!path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) return path;

        try
        {
            var link = (IShellLinkW)new ShellLink();
            ((ComTypes.IPersistFile)link).Load(path, 0 /* STGM_READ */);
            var sb = new StringBuilder(260);
            var data = new WIN32_FIND_DATAW();
            link.GetPath(sb, sb.Capacity, ref data, SLGP_RAWPATH);
            var target = sb.ToString();
            if (string.IsNullOrEmpty(target) || HasSuspiciousPathChars(target))
                return path; // empty or crafted (non-filesystem) target → keep the .lnk path, don't trust it
            return target;
        }
        catch
        {
            return path; // unreadable or non-filesystem shortcut → keep the original path
        }
    }

    /// <summary>True if the string contains a character that never occurs in a legitimate Windows path
    /// (a double quote, the name-illegal &lt; &gt; | characters, or a control character). A .lnk's stored
    /// target is attacker-controlled binary data, so such a character marks a crafted shortcut trying to
    /// smuggle extra command-line tokens rather than name a real file. Note: Path.GetInvalidPathChars on
    /// .NET no longer includes the quote, so the dangerous set is checked explicitly here.</summary>
    private static bool HasSuspiciousPathChars(string s)
    {
        foreach (var c in s)
            if (c is '"' or '<' or '>' or '|' || c < ' ') return true;
        return false;
    }

    private const uint SLGP_RAWPATH = 0x4;

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink { }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown),
     Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile,
            int cchMaxPath, ref WIN32_FIND_DATAW pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath,
            int cchIconPath, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WIN32_FIND_DATAW
    {
        public uint dwFileAttributes;
        public ComTypes.FILETIME ftCreationTime;
        public ComTypes.FILETIME ftLastAccessTime;
        public ComTypes.FILETIME ftLastWriteTime;
        public uint nFileSizeHigh;
        public uint nFileSizeLow;
        public uint dwReserved0;
        public uint dwReserved1;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string cFileName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)] public string cAlternateFileName;
    }
}
