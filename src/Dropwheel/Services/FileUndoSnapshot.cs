using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;

namespace Dropwheel.Services;

/// <summary>Cheap identity-and-metadata snapshot used to ensure app-level Undo never acts on a file
/// that has been replaced or edited since Dropwheel created or moved it.</summary>
internal readonly record struct FileUndoSnapshot(
    string Path,
    bool IsDirectory,
    uint VolumeSerial,
    ulong FileId,
    long Length,
    long LastWriteUtc,
    byte[]? ContentHash)
{
    private const uint GenericRead = 0x80000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint FileShareDelete = 0x00000004;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const uint FileAttributeDirectory = 0x00000010;
    private const long MaxHashedFileBytes = 64L * 1024 * 1024;

    internal static FileUndoSnapshot? Capture(string path)
    {
        try
        {
            var expectedDirectory = Directory.Exists(path);
            using var handle = CreateFile(
                path,
                expectedDirectory ? 0 : GenericRead,
                expectedDirectory
                    ? FileShareRead | FileShareWrite | FileShareDelete
                    : FileShareRead | FileShareDelete,
                IntPtr.Zero,
                OpenExisting,
                FileFlagBackupSemantics,
                IntPtr.Zero);
            if (handle.IsInvalid || !GetFileInformationByHandle(handle, out var before)) return null;

            var isDirectory = (before.FileAttributes & FileAttributeDirectory) != 0;
            var length = ((long)before.FileSizeHigh << 32) | before.FileSizeLow;
            byte[]? contentHash = null;
            if (!isDirectory)
            {
                // Metadata timestamps can remain unchanged after an immediate same-length rewrite.
                // Hash bounded files exactly; decline app-level Undo for larger files rather than risk
                // deleting content whose unchanged identity/length cannot prove it is still our copy.
                if (length < 0 || length > MaxHashedFileBytes) return null;
                contentHash = Hash(handle, length);
                if (contentHash == null
                    || !GetFileInformationByHandle(handle, out var after)
                    || !SameMetadata(before, after))
                    return null;
            }

            return new FileUndoSnapshot(
                System.IO.Path.GetFullPath(path),
                isDirectory,
                before.VolumeSerialNumber,
                ((ulong)before.FileIndexHigh << 32) | before.FileIndexLow,
                length,
                ((long)before.LastWriteTime.High << 32) | before.LastWriteTime.Low,
                contentHash);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    internal bool MatchesCurrent()
    {
        var current = Capture(Path);
        return current is { } value
            && value.IsDirectory == IsDirectory
            && value.VolumeSerial == VolumeSerial
            && value.FileId == FileId
            && value.Length == Length
            && value.LastWriteUtc == LastWriteUtc
            && (IsDirectory
                || ContentHash != null
                    && value.ContentHash != null
                    && ContentHash.AsSpan().SequenceEqual(value.ContentHash));
    }

    private static byte[]? Hash(SafeFileHandle handle, long length)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[128 * 1024];
        long offset = 0;
        while (offset < length)
        {
            var read = RandomAccess.Read(handle, buffer, offset);
            if (read <= 0) return null;
            hash.AppendData(buffer, 0, read);
            offset += read;
        }
        return hash.GetHashAndReset();
    }

    private static bool SameMetadata(
        ByHandleFileInformation left,
        ByHandleFileInformation right) =>
        left.FileAttributes == right.FileAttributes
        && left.VolumeSerialNumber == right.VolumeSerialNumber
        && left.FileSizeHigh == right.FileSizeHigh
        && left.FileSizeLow == right.FileSizeLow
        && left.FileIndexHigh == right.FileIndexHigh
        && left.FileIndexLow == right.FileIndexLow
        && left.LastWriteTime.High == right.LastWriteTime.High
        && left.LastWriteTime.Low == right.LastWriteTime.Low;

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public NativeFileTime CreationTime;
        public NativeFileTime LastAccessTime;
        public NativeFileTime LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeFileTime
    {
        public uint Low;
        public uint High;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(
        SafeFileHandle file,
        out ByHandleFileInformation information);
}
