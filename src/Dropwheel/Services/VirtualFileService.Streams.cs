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

    private static bool SaveContents(IComData com, int index, string path)
    {
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
            if (med.tymed == TYMED.TYMED_ISTREAM) { SaveIStream(med.unionmember, path); return true; }
            if (med.tymed == TYMED.TYMED_HGLOBAL) { SaveHGlobal(med.unionmember, path); return true; }
            return false;
        }
        finally { ReleaseStgMedium(ref med); }
    }

    private static void SaveIStream(IntPtr punk, string path)
    {
        var stream = (IStream)Marshal.GetObjectForIUnknown(punk);
        try
        {
            using var fs = File.Create(path);
            var buf = new byte[81920];
            IntPtr pRead = Marshal.AllocHGlobal(4);
            try
            {
                while (true)
                {
                    stream.Read(buf, buf.Length, pRead);
                    int read = Marshal.ReadInt32(pRead);
                    if (read <= 0) break;
                    fs.Write(buf, 0, read);
                }
            }
            finally { Marshal.FreeHGlobal(pRead); }
        }
        finally { Marshal.ReleaseComObject(stream); }
    }

    /// <summary>Saves content from an HGLOBAL. Note: GlobalSize returns the allocated block size,
    /// which may be rounded up beyond the real data length, so a few extra trailing bytes can end up
    /// in the file. HGLOBAL for CFSTR_FILECONTENTS has no reliable "real length" field, and trimming
    /// trailing zeros is wrong (a legitimate binary also contains zeros). In practice sources deliver
    /// files via ISTREAM (the branch above); this is a rare fallback.</summary>
    private static void SaveHGlobal(IntPtr h, string path)
    {
        var p = GlobalLock(h);
        if (p == IntPtr.Zero) return;
        try
        {
            var buf = new byte[(long)GlobalSize(h)];
            Marshal.Copy(p, buf, 0, buf.Length);
            File.WriteAllBytes(path, buf);
        }
        finally { GlobalUnlock(h); }
    }

    private static string UniquePath(string folder, string name)
    {
        name = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
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
