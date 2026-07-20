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
    internal const long MaxVirtualFileBytes = 512L * 1024 * 1024;
    internal const long MaxVirtualBatchBytes = 1024L * 1024 * 1024;
    internal const int VirtualCopyBufferBytes = 81920;

    private readonly record struct ContentSaveResult(long BytesConsumed, bool Saved);

    private sealed class ContentCommitState
    {
        internal object Gate { get; } = new();
        internal bool Committed { get; set; }
        internal ContentSaveResult Result { get; set; }
    }

    private static ContentSaveResult SaveContents(
        IComData com,
        int index,
        string path,
        long maxBytes,
        ContentCommitState commit,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
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
            cancellationToken.ThrowIfCancellationRequested();
            if (med.unionmember == IntPtr.Zero) return default; // the source gave no medium for this index
            var result = med.tymed switch
            {
                TYMED.TYMED_ISTREAM => SaveIStream(med.unionmember, tmp, maxBytes, cancellationToken),
                TYMED.TYMED_HGLOBAL => SaveHGlobal(med.unionmember, tmp, maxBytes, cancellationToken),
                _ => default,
            };
            if (!result.Saved) return result;
            lock (commit.Gate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                File.Move(tmp, path);
                commit.Result = result;
                commit.Committed = true;
            }
            return result;
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            ReleaseStgMedium(ref med);
        }
    }

    private static ContentSaveResult SaveIStream(
        IntPtr punk,
        string path,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var stream = (IStream)Marshal.GetObjectForIUnknown(punk);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var fs = File.Create(path);
            var buf = new byte[VirtualCopyBufferBytes];
            long total = 0;
            IntPtr pRead = Marshal.AllocHGlobal(4);
            try
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    stream.Read(buf, buf.Length, pRead);
                    cancellationToken.ThrowIfCancellationRequested();
                    int read = Marshal.ReadInt32(pRead);
                    if (read <= 0) break;
                    total += read;
                    if (total > maxBytes)
                    {
                        ErrorLog.Write($"Virtual file exceeded {maxBytes} bytes; aborting to avoid an unbounded write");
                        // Charge the full allowance even though the partial temp file is deleted. Otherwise
                        // thousands of oversized items could each consume the per-file allowance.
                        return new ContentSaveResult(maxBytes, Saved: false);
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                    fs.Write(buf, 0, read);
                    cancellationToken.ThrowIfCancellationRequested();
                }
            }
            finally { Marshal.FreeHGlobal(pRead); }
            return new ContentSaveResult(total, Saved: true);
        }
        finally { Marshal.ReleaseComObject(stream); }
    }

    /// <summary>Saves content from an HGLOBAL. Note: GlobalSize returns the allocated block size,
    /// which may be rounded up beyond the real data length, so a few extra trailing bytes can end up
    /// in the file. HGLOBAL for CFSTR_FILECONTENTS has no reliable "real length" field, and trimming
    /// trailing zeros is wrong (a legitimate binary also contains zeros). In practice sources deliver
    /// files via ISTREAM (the branch above); this is a rare fallback.</summary>
    private static ContentSaveResult SaveHGlobal(
        IntPtr h,
        string path,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var p = GlobalLock(h);
        if (p == IntPtr.Zero) return default;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            long size = (long)GlobalSize(h);
            if (size > maxBytes)
            {
                ErrorLog.Write($"Virtual file HGLOBAL of {size} bytes exceeds the cap; skipped");
                return new ContentSaveResult(maxBytes, Saved: false);
            }
            using var fs = File.Create(path);
            var buf = new byte[Math.Min(VirtualCopyBufferBytes, checked((int)size))];
            long offset = 0;
            while (offset < size)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var count = (int)Math.Min(buf.Length, size - offset);
                Marshal.Copy(IntPtr.Add(p, checked((int)offset)), buf, 0, count);
                cancellationToken.ThrowIfCancellationRequested();
                fs.Write(buf, 0, count);
                offset += count;
            }
            cancellationToken.ThrowIfCancellationRequested();
            return new ContentSaveResult(size, Saved: true);
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
