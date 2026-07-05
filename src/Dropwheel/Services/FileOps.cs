using System.Runtime.InteropServices;
using Dropwheel.Models;

namespace Dropwheel.Services;

/// <summary>Копирование/перемещение через SHFileOperation: системный прогресс,
/// конфликт-диалоги и отмена через Корзину (FOF_ALLOWUNDO) бесплатно.</summary>
public static class FileOps
{
    private const uint FO_MOVE = 0x0001, FO_COPY = 0x0002;
    private const ushort FOF_ALLOWUNDO = 0x0040, FOF_NOCONFIRMMKDIR = 0x0200;

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

    public static bool Execute(IEnumerable<string> files, string destFolder, DropAction action)
    {
        var op = new SHFILEOPSTRUCT
        {
            wFunc  = action == DropAction.Move ? FO_MOVE : FO_COPY,
            pFrom  = string.Join("\0", files) + "\0\0",
            pTo    = destFolder + "\0\0",
            fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMMKDIR,
        };
        return SHFileOperation(ref op) == 0 && !op.fAnyOperationsAborted;
    }
}
