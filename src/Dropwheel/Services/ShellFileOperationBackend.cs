using System.IO;
using System.Runtime.InteropServices;
using Dropwheel.Models;
using Dropwheel.Services.Interop;

namespace Dropwheel.Services;

/// <summary>Windows IFileOperation interop isolated from the public high-level file API.</summary>
internal static class ShellFileOperationBackend
{
    private const uint FOF_ALLOWUNDO = 0x0040;
    private const uint FOF_NOCONFIRMMKDIR = 0x0200;
    private const uint FOF_NOCONFIRMATION = 0x0010;
    private const uint FOF_SILENT = 0x0004;
    private const uint FOF_RENAMEONCOLLISION = 0x0008;
    private const uint FOF_NOERRORUI = 0x0400;
    private const uint FOFX_RECYCLEONDELETE = 0x00080000;
    private const uint FOFX_ADDUNDORECORD = 0x20000000;
    private const uint SIGDN_FILESYSPATH = 0x80058000;
    private static readonly Guid ShellItemId = typeof(IShellItem).GUID;
    private static readonly Guid FileOperationClassId =
        new("3AD05575-8857-4850-9277-11B85BDB8E09");
    private static readonly Guid FileOperationId = typeof(IFileOperation).GUID;

    internal static FileOperationResult Execute(
        IReadOnlyList<FileOperationCandidate> candidates,
        DropAction action,
        bool silent,
        ConflictPolicy policy)
    {
        IFileOperation? operation = null;
        var shellItems = new List<object>();
        var sinks = new List<ItemProgressSink>();
        var performSucceeded = false;
        var aborted = false;
        try
        {
            operation = CreateFileOperation();
            ThrowIfFailed(operation.SetOperationFlags(OperationFlags(silent, policy)));
            foreach (var candidate in candidates)
            {
                var source = CreateShellItem(candidate.Source);
                shellItems.Add(source);
                var destinationParent = Path.GetDirectoryName(candidate.Destination)
                    ?? throw new InvalidOperationException("Destination has no parent folder.");
                Directory.CreateDirectory(destinationParent);
                var destinationFolder = CreateShellItem(destinationParent);
                shellItems.Add(destinationFolder);
                var sink = new ItemProgressSink(candidate);
                sinks.Add(sink);
                var destinationName = Path.GetFileName(candidate.Destination);
                ThrowIfFailed(action == DropAction.Move
                    ? operation.MoveItem(source, destinationFolder, destinationName, sink)
                    : operation.CopyItem(source, destinationFolder, destinationName, sink));
            }

            performSucceeded = operation.PerformOperations() >= 0;
            ThrowIfFailed(operation.GetAnyOperationsAborted(out aborted));
        }
        catch (Exception ex) when (ex is COMException or IOException
            or UnauthorizedAccessException or InvalidOperationException)
        {
            ErrorLog.Write("Windows shell file operation failed", ex);
        }
        finally
        {
            foreach (var item in shellItems)
                if (Marshal.IsComObject(item)) Marshal.FinalReleaseComObject(item);
            if (operation != null && Marshal.IsComObject(operation))
                Marshal.FinalReleaseComObject(operation);
        }

        var completed = sinks.Where(sink => sink.Succeeded).ToArray();
        var changes = completed
            .Where(sink => sink.ActualDestination != null
                && (!sink.Candidate.DestinationExisted
                    || !string.Equals(
                        Path.GetFullPath(sink.Candidate.Destination),
                        Path.GetFullPath(sink.ActualDestination),
                        StringComparison.OrdinalIgnoreCase)))
            .Select(sink => new FileOperationChange(
                sink.Candidate.Source,
                sink.ActualDestination!))
            .ToArray();
        var status = performSucceeded && !aborted && completed.Length == candidates.Count
            ? FileOperationStatus.Succeeded
            : completed.Length > 0
                ? FileOperationStatus.PartiallySucceeded
                : aborted ? FileOperationStatus.Cancelled : FileOperationStatus.Failed;
        return new FileOperationResult(status, candidates.Count, completed.Length, changes);
    }

    private static uint OperationFlags(bool silent, ConflictPolicy policy)
    {
        var flags = FOF_ALLOWUNDO | FOF_NOCONFIRMMKDIR | FOFX_ADDUNDORECORD;
        if (silent)
        {
            flags |= FOF_SILENT | FOF_NOERRORUI;
            flags |= policy == ConflictPolicy.Overwrite
                ? FOF_NOCONFIRMATION
                : FOF_RENAMEONCOLLISION;
        }
        else
        {
            flags |= ConflictFlags(policy);
        }
        return flags;
    }

    internal static uint ConflictFlags(ConflictPolicy policy) => policy switch
    {
        ConflictPolicy.KeepBoth => FOF_RENAMEONCOLLISION,
        ConflictPolicy.Overwrite => FOF_NOCONFIRMATION,
        _ => 0,
    };

    internal static bool Delete(IEnumerable<string> paths)
    {
        var list = paths.ToArray();
        if (list.Length == 0) return true;
        IFileOperation? operation = null;
        var items = new List<IShellItem>();
        try
        {
            operation = CreateFileOperation();
            ThrowIfFailed(operation.SetOperationFlags(
                FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOFX_RECYCLEONDELETE | FOFX_ADDUNDORECORD));
            foreach (var path in list)
            {
                var item = CreateShellItem(path);
                items.Add(item);
                ThrowIfFailed(operation.DeleteItem(item, null));
            }
            ThrowIfFailed(operation.PerformOperations());
            ThrowIfFailed(operation.GetAnyOperationsAborted(out var aborted));
            return !aborted;
        }
        catch (Exception ex) when (ex is COMException or IOException
            or UnauthorizedAccessException or InvalidOperationException)
        {
            ErrorLog.Write("Windows shell delete failed", ex);
            return false;
        }
        finally
        {
            foreach (var item in items)
                if (Marshal.IsComObject(item)) Marshal.FinalReleaseComObject(item);
            if (operation != null && Marshal.IsComObject(operation))
                Marshal.FinalReleaseComObject(operation);
        }
    }

    private static void ThrowIfFailed(int result)
    {
        if (result < 0) Marshal.ThrowExceptionForHR(result);
    }

    private static IShellItem CreateShellItem(string path)
    {
        var id = ShellItemId;
        ThrowIfFailed(SHCreateItemFromParsingName(
            Path.GetFullPath(path),
            IntPtr.Zero,
            ref id,
            out var item));
        return item;
    }

    private static IFileOperation CreateFileOperation()
    {
        var classId = FileOperationClassId;
        var interfaceId = FileOperationId;
        ThrowIfFailed(CoCreateInstance(
            ref classId,
            IntPtr.Zero,
            1,
            ref interfaceId,
            out var operation));
        return operation;
    }

    private static string? FileSystemPath(IShellItem? item)
    {
        if (item == null) return null;
        var result = item.GetDisplayName(SIGDN_FILESYSPATH, out var value);
        if (result < 0 || value == IntPtr.Zero) return null;
        try { return Marshal.PtrToStringUni(value); }
        finally { Marshal.FreeCoTaskMem(value); }
    }

    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    private sealed class ItemProgressSink(FileOperationCandidate candidate)
        : IFileOperationProgressSink
    {
        internal FileOperationCandidate Candidate { get; } = candidate;
        internal bool Succeeded { get; private set; }
        internal string? ActualDestination { get; private set; }

        private int Complete(int result, IShellItem? newItem)
        {
            if (result >= 0 && newItem != null)
            {
                ActualDestination = FileSystemPath(newItem);
                Succeeded = ActualDestination != null;
            }
            return 0;
        }

        public int StartOperations() => 0;
        public int FinishOperations(int result) => 0;
        public int PreRenameItem(uint flags, IShellItem item, string newName) => 0;
        public int PostRenameItem(uint flags, IShellItem item, string newName, int result, IShellItem newItem) => Complete(result, newItem);
        public int PreMoveItem(uint flags, IShellItem item, IShellItem destination, string? newName) => 0;
        public int PostMoveItem(uint flags, IShellItem item, IShellItem destination, string? newName, int result, IShellItem newItem) => Complete(result, newItem);
        public int PreCopyItem(uint flags, IShellItem item, IShellItem destination, string? newName) => 0;
        public int PostCopyItem(uint flags, IShellItem item, IShellItem destination, string? newName, int result, IShellItem newItem) => Complete(result, newItem);
        public int PreDeleteItem(uint flags, IShellItem item) => 0;
        public int PostDeleteItem(uint flags, IShellItem item, int result, IShellItem newItem) => Complete(result, newItem);
        public int PreNewItem(uint flags, IShellItem destination, string newName) => 0;
        public int PostNewItem(uint flags, IShellItem destination, string newName, string templateName, uint attributes, int result, IShellItem newItem) => Complete(result, newItem);
        public int UpdateProgress(uint totalWork, uint workSoFar) => 0;
        public int ResetTimer() => 0;
        public int PauseTimer() => 0;
        public int ResumeTimer() => 0;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(
        string path,
        IntPtr bindContext,
        ref Guid interfaceId,
        [MarshalAs(UnmanagedType.Interface)] out IShellItem shellItem);

    [DllImport("ole32.dll", PreserveSig = true)]
    private static extern int CoCreateInstance(
        ref Guid classId,
        IntPtr outer,
        uint context,
        ref Guid interfaceId,
        [MarshalAs(UnmanagedType.Interface)] out IFileOperation operation);

    [ComImport]
    [Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOperation
    {
        [PreserveSig] int Advise(IFileOperationProgressSink sink, out uint cookie);
        [PreserveSig] int Unadvise(uint cookie);
        [PreserveSig] int SetOperationFlags(uint flags);
        [PreserveSig] int SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string message);
        [PreserveSig] int SetProgressDialog([MarshalAs(UnmanagedType.Interface)] object dialog);
        [PreserveSig] int SetProperties(IntPtr properties);
        [PreserveSig] int SetOwnerWindow(IntPtr owner);
        [PreserveSig] int ApplyPropertiesToItem(IShellItem item);
        [PreserveSig] int ApplyPropertiesToItems([MarshalAs(UnmanagedType.Interface)] object items);
        [PreserveSig] int RenameItem(IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string newName, IFileOperationProgressSink? sink);
        [PreserveSig] int RenameItems([MarshalAs(UnmanagedType.Interface)] object items, [MarshalAs(UnmanagedType.LPWStr)] string newName);
        [PreserveSig] int MoveItem(IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string? newName, IFileOperationProgressSink? sink);
        [PreserveSig] int MoveItems([MarshalAs(UnmanagedType.Interface)] object items, IShellItem destination);
        [PreserveSig] int CopyItem(IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string? newName, IFileOperationProgressSink? sink);
        [PreserveSig] int CopyItems([MarshalAs(UnmanagedType.Interface)] object items, IShellItem destination);
        [PreserveSig] int DeleteItem(IShellItem item, IFileOperationProgressSink? sink);
        [PreserveSig] int DeleteItems([MarshalAs(UnmanagedType.Interface)] object items);
        [PreserveSig] int NewItem(IShellItem destination, uint attributes, [MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.LPWStr)] string? templateName, IFileOperationProgressSink? sink);
        [PreserveSig] int PerformOperations();
        [PreserveSig] int GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool aborted);
    }

}
