using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using IComData = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace Dropwheel.Services;

public static partial class VirtualFileService
{
    [DllImport("ole32.dll")] private static extern void ReleaseStgMedium(ref STGMEDIUM medium);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr h);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr h);
    [DllImport("kernel32.dll")] private static extern nint GlobalSize(IntPtr h);

    /// <summary>Upper bound on a single virtual file written to disk. A malicious drag source can return
    /// an IStream that never ends; without a cap that would fill the disk. Generous enough for any real
    /// dropped attachment, so it only ever stops a runaway source.</summary>
    internal const long MaxVirtualFileBytes = 2L * 1024 * 1024 * 1024;

    private static bool SaveContents(IComData com, int index, string path)
    {
        var tmp = TempPathFor(path);
        var fmt = new FORMATETC
        {
            cfFormat = (short)System.Windows.DataFormats.GetDataFormat(ContentsFormat).Id,
            dwAspect = DVASPECT.DVASPECT_CONTENT,
            lindex = index, // item index in FILEGROUPDESCRIPTOR
            tymed = TYMED.TYMED_ISTREAM | TYMED.TYMED_HGLOBAL,
        };
        com.GetData(ref fmt, out STGMEDIUM med);
        try
        {
            if (med.unionmember == IntPtr.Zero) return false; // the source gave no medium for this index
            var saved = med.tymed switch
            {
                TYMED.TYMED_ISTREAM => SaveIStream(med.unionmember, tmp),
                TYMED.TYMED_HGLOBAL => SaveHGlobal(med.unionmember, tmp),
                _ => false,
            };
            if (!saved) return false;
            File.Move(tmp, path);
            return true;
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            ReleaseStgMedium(ref med);
        }
    }

    private static bool SaveIStream(IntPtr punk, string path)
    {
        var stream = (IStream)Marshal.GetObjectForIUnknown(punk);
        try
        {
            using var fs = File.Create(path);
            var buf = new byte[81920];
            long total = 0;
            IntPtr pRead = Marshal.AllocHGlobal(4);
            try
            {
                while (true)
                {
                    stream.Read(buf, buf.Length, pRead);
                    int read = Marshal.ReadInt32(pRead);
                    if (read <= 0) break;
                    total += read;
                    if (total > MaxVirtualFileBytes)
                    {
                        ErrorLog.Write($"Virtual file exceeded {MaxVirtualFileBytes} bytes; aborting to avoid an unbounded write");
                        return false; // SaveContents' finally deletes the partial temp file
                    }
                    fs.Write(buf, 0, read);
                }
            }
            finally { Marshal.FreeHGlobal(pRead); }
            return true;
        }
        finally { Marshal.ReleaseComObject(stream); }
    }

    /// <summary>Saves content from an HGLOBAL. Note: GlobalSize returns the allocated block size,
    /// which may be rounded up beyond the real data length, so a few extra trailing bytes can end up
    /// in the file. HGLOBAL for CFSTR_FILECONTENTS has no reliable "real length" field, and trimming
    /// trailing zeros is wrong (a legitimate binary also contains zeros). In practice sources deliver
    /// files via ISTREAM (the branch above); this is a rare fallback.</summary>
    private static bool SaveHGlobal(IntPtr h, string path)
    {
        var p = GlobalLock(h);
        if (p == IntPtr.Zero) return false;
        try
        {
            long size = (long)GlobalSize(h);
            if (size > MaxVirtualFileBytes)
            {
                ErrorLog.Write($"Virtual file HGLOBAL of {size} bytes exceeds the cap; skipped");
                return false;
            }
            var buf = new byte[size];
            Marshal.Copy(p, buf, 0, buf.Length);
            File.WriteAllBytes(path, buf);
            return true;
        }
        finally { GlobalUnlock(h); }
    }

    internal static string TempPathFor(string path) =>
        Path.Combine(Path.GetDirectoryName(path) ?? "", $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

    internal static string UniquePath(string folder, string name)
    {
        name = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
        if (string.IsNullOrWhiteSpace(name)) name = "file";
        var path = Path.Combine(folder, name);
        if (!File.Exists(path) && !Directory.Exists(path)) return path;
        string stem = Path.GetFileNameWithoutExtension(name), ext = Path.GetExtension(name);
        for (int i = 2; ; i++)
        {
            path = Path.Combine(folder, $"{stem} ({i}){ext}");
            if (!File.Exists(path) && !Directory.Exists(path)) return path;
        }
    }
}
