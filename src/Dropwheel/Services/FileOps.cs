using System.IO;
using System.Runtime.InteropServices;
using Dropwheel.Models;

namespace Dropwheel.Services;

/// <summary>Copy/move via SHFileOperation: system progress UI, conflict dialogs
/// and Recycle Bin undo (FOF_ALLOWUNDO) come for free.</summary>
public static class FileOps
{
    private const uint FO_MOVE = 0x0001, FO_COPY = 0x0002, FO_DELETE = 0x0003;
    private const ushort FOF_ALLOWUNDO = 0x0040, FOF_NOCONFIRMMKDIR = 0x0200, FOF_NOCONFIRMATION = 0x0010,
        FOF_SILENT = 0x0004, FOF_RENAMEONCOLLISION = 0x0008, FOF_NOERRORUI = 0x0400;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT op);

    public static string[] DestinationConflicts(IEnumerable<string> files, string destFolder)
    {
        return files
            .Select(Path.GetFileName)
            .Where(n => !string.IsNullOrEmpty(n))
            .Select(n => Path.Combine(destFolder, n!))
            .Where(p => File.Exists(p) || Directory.Exists(p))
            .ToArray();
    }

    /// <summary>Copy or move files into destFolder. When silent (used by the folder watcher for
    /// auto-sort) the shell shows no progress window, no error UI and no conflict prompt. Callers
    /// that need no-overwrite behavior must preflight with DestinationConflicts first.</summary>
    public static bool Execute(IEnumerable<string> files, string destFolder, DropAction action, bool silent = false)
    {
        var list = files.ToArray();
        if (list.Length == 0) return true; // nothing to do — don't call SHFileOperation with an empty list
        ushort flags = FOF_ALLOWUNDO | FOF_NOCONFIRMMKDIR;
        if (silent) flags |= (ushort)(FOF_SILENT | FOF_NOERRORUI | FOF_NOCONFIRMATION | FOF_RENAMEONCOLLISION);
        var op = new SHFILEOPSTRUCT
        {
            wFunc = action == DropAction.Move ? FO_MOVE : FO_COPY,
            pFrom = string.Join("\0", list) + "\0\0",
            pTo = destFolder + "\0\0",
            fFlags = flags,
        };
        return SHFileOperation(ref op) == 0 && !op.fAnyOperationsAborted;
    }

    public static bool HasDestinationCollision(IEnumerable<string> sources, string destFolder)
        => DestinationConflicts(sources, destFolder).Length > 0;

    /// <summary>Delete to Recycle Bin without confirmation (for Undo after a copy).</summary>
    public static bool Delete(IEnumerable<string> paths)
    {
        var list = paths.ToArray();
        if (list.Length == 0) return true;
        var op = new SHFILEOPSTRUCT
        {
            wFunc = FO_DELETE,
            pFrom = string.Join("\0", list) + "\0\0",
            pTo = "\0\0",
            fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION,
        };
        return SHFileOperation(ref op) == 0 && !op.fAnyOperationsAborted;
    }
}
