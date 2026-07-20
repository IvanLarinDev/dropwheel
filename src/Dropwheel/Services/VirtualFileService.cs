using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;

namespace Dropwheel.Services;

/// <summary>Accepts "virtual" files (Outlook attachments, browser images):
/// CFSTR_FILEDESCRIPTORW + CFSTR_FILECONTENTS formats. Copy is the only possible action.</summary>
public static partial class VirtualFileService
{
    private const int MaxDescriptorItems = 4096;
    internal const int MaxDescriptorBytes = 4 + MaxDescriptorItems * 592;
    private const string DescriptorFormat = "FileGroupDescriptorW";
    private const string ContentsFormat = "FileContents";
    private static readonly TimeSpan ContentReadTimeout = TimeSpan.FromSeconds(10);
    private static readonly SemaphoreSlim ContentReadGate = new(1, 1);
    private static readonly ConditionalWeakTable<IDataObject, DescriptorProbe> DescriptorProbes = new();

    /// <summary>Returns a cached probe result and never calls an external OLE provider on the caller thread.</summary>
    public static bool HasVirtualFiles(IDataObject data)
    {
        var read = DescriptorReadFor(data);
        return read.IsCompletedSuccessfully && read.Result.Names is { Length: > 0 };
    }

    /// <summary>Saves all virtual files into a folder without blocking the UI on an untrusted COM stream.</summary>
    public static async Task<string[]> ExtractAsync(IDataObject data, string destFolder)
    {
        var names = (await DescriptorReadFor(data).ConfigureAwait(false)).Names ?? Array.Empty<string>();
        if (names.Length == 0) return Array.Empty<string>();
        if (data is not System.Runtime.InteropServices.ComTypes.IDataObject com)
            return Array.Empty<string>();

        var saved = new List<string>();
        var budget = new VirtualBatchBudget();
        for (int i = 0; i < names.Length; i++)
        {
            var allowance = budget.Reserve();
            if (allowance <= 0)
            {
                ErrorLog.Write($"Virtual-file batch reached its {MaxVirtualBatchBytes}-byte cap; remaining items were skipped");
                break;
            }

            var path = UniquePath(destFolder, names[i]);
            var attempt = await SaveWithTimeoutAsync(com, i, path, allowance, names[i]).ConfigureAwait(false);
            if (attempt.Status == ContentSaveStatus.Completed)
            {
                budget.Complete(allowance, attempt.Result.BytesConsumed);
                if (attempt.Result.Saved) saved.Add(path);
                continue;
            }

            // The reservation remains charged after a failure. A hostile source cannot repeatedly
            // throw after large partial reads and thereby bypass the batch limit.
            if (attempt.Status is ContentSaveStatus.TimedOut or ContentSaveStatus.GateUnavailable) break;
        }
        return saved.ToArray();
    }

    private static Task<DescriptorReadResult> DescriptorReadFor(IDataObject data) =>
        DescriptorProbes.GetValue(data, static value => new DescriptorProbe(value)).Read.Value;

    private static async Task<DescriptorReadResult> ReadDescriptorWithTimeoutAsync(IDataObject data)
    {
        if (!await ContentReadGate.WaitAsync(ContentReadTimeout).ConfigureAwait(false))
        {
            ErrorLog.Write("Virtual-file descriptor probe is waiting for an unresponsive COM source; payload was skipped");
            return default;
        }

        Task<string[]>? readTask = null;
        bool workerOwnsGate = false;
        try
        {
            readTask = Task.Run(() =>
            {
                try
                {
                    return ReadNames(data);
                }
                finally
                {
                    ContentReadGate.Release();
                }
            });
            workerOwnsGate = true;
            var names = await readTask.WaitAsync(ContentReadTimeout).ConfigureAwait(false);
            return new DescriptorReadResult(names);
        }
        catch (TimeoutException) when (readTask is not null)
        {
            ObserveLateFault(readTask);
            ErrorLog.Write($"Virtual-file descriptor did not respond within {ContentReadTimeout.TotalSeconds:0} seconds; payload was skipped");
            return default;
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Could not read virtual-file descriptor", ex);
            return default;
        }
        finally
        {
            if (!workerOwnsGate) ContentReadGate.Release();
        }
    }

    private static async Task<ContentSaveAttempt> SaveWithTimeoutAsync(
        System.Runtime.InteropServices.ComTypes.IDataObject com,
        int index,
        string path,
        long allowance,
        string displayName)
    {
        if (!await ContentReadGate.WaitAsync(ContentReadTimeout).ConfigureAwait(false))
        {
            ErrorLog.Write("Virtual-file extraction is still waiting for an unresponsive COM source; remaining items were skipped");
            return new ContentSaveAttempt(ContentSaveStatus.GateUnavailable, default);
        }

        using var timeout = new CancellationTokenSource();
        var commit = new ContentCommitState();
        Task<ContentSaveResult>? saveTask = null;
        bool workerOwnsGate = false;
        try
        {
            saveTask = Task.Run(() =>
            {
                try
                {
                    return SaveContents(com, index, path, allowance, commit, timeout.Token);
                }
                finally
                {
                    ContentReadGate.Release();
                }
            });
            workerOwnsGate = true;
            var result = await saveTask.WaitAsync(ContentReadTimeout).ConfigureAwait(false);
            return new ContentSaveAttempt(ContentSaveStatus.Completed, result);
        }
        catch (TimeoutException) when (saveTask is not null)
        {
            ContentSaveResult? committedResult;
            lock (commit.Gate)
            {
                committedResult = commit.Committed ? commit.Result : null;
                if (!commit.Committed) timeout.Cancel();
            }

            ObserveLateFault(saveTask);
            if (committedResult is { } result)
                return new ContentSaveAttempt(ContentSaveStatus.Completed, result);

            ErrorLog.Write($"Virtual file '{displayName}' did not respond within {ContentReadTimeout.TotalSeconds:0} seconds; remaining items were skipped");
            return new ContentSaveAttempt(ContentSaveStatus.TimedOut, default);
        }
        catch (Exception ex)
        {
            lock (commit.Gate)
            {
                if (commit.Committed)
                    return new ContentSaveAttempt(ContentSaveStatus.Completed, commit.Result);
            }
            ErrorLog.Write($"Could not save virtual file '{displayName}'", ex);
            return new ContentSaveAttempt(ContentSaveStatus.Failed, default);
        }
        finally
        {
            if (!workerOwnsGate) ContentReadGate.Release();
        }
    }

    private static void ObserveLateFault(Task task) =>
        _ = task.ContinueWith(
            completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    private sealed class DescriptorProbe
    {
        internal DescriptorProbe(IDataObject data) =>
            Read = new Lazy<Task<DescriptorReadResult>>(
                () => ReadDescriptorWithTimeoutAsync(data),
                LazyThreadSafetyMode.ExecutionAndPublication);

        internal Lazy<Task<DescriptorReadResult>> Read { get; }
    }

    private readonly record struct DescriptorReadResult(string[]? Names);

    private enum ContentSaveStatus { Completed, Failed, TimedOut, GateUnavailable }

    private readonly record struct ContentSaveAttempt(ContentSaveStatus Status, ContentSaveResult Result);

    internal sealed class VirtualBatchBudget
    {
        internal long UsedBytes { get; private set; }

        internal long Reserve()
        {
            var allowance = Math.Min(MaxVirtualFileBytes, MaxVirtualBatchBytes - UsedBytes);
            if (allowance > 0) UsedBytes += allowance;
            return allowance;
        }

        internal void Complete(long allowance, long bytesConsumed)
        {
            var charged = Math.Clamp(bytesConsumed, 0, allowance);
            UsedBytes -= allowance - charged;
        }
    }

    private static string[] ReadNames(IDataObject data)
    {
        if (!data.GetDataPresent(DescriptorFormat) || !data.GetDataPresent(ContentsFormat))
            return Array.Empty<string>();
        if (data.GetData(DescriptorFormat) is not MemoryStream ms) return Array.Empty<string>();
        if (ms.Length > MaxDescriptorBytes) return Array.Empty<string>();
        return ParseDescriptorNames(ms.ToArray());
    }

    // FILEGROUPDESCRIPTORW: UINT cItems; FILEDESCRIPTORW[cItems] (592 bytes each,
    // cFileName is WCHAR[260] at offset 72).
    /// <summary>Extracts the file names from a raw FILEGROUPDESCRIPTORW buffer. Pure so it can be
    /// tested directly. The item count comes from an external drag source, so an out-of-range value
    /// (corrupt data) yields an empty result instead of throwing or over-allocating.</summary>
    internal static string[] ParseDescriptorNames(byte[] buf)
    {
        if (buf.Length < 4 || buf.Length > MaxDescriptorBytes) return Array.Empty<string>();
        int count = BitConverter.ToInt32(buf, 0);
        if (count <= 0 || count > MaxDescriptorItems) return Array.Empty<string>();
        const int EntrySize = 592, NameOffset = 72, NameBytes = 520;
        var names = new List<string>();
        for (int i = 0; i < count; i++)
        {
            int off = 4 + i * EntrySize + NameOffset;
            if (off + NameBytes > buf.Length) break;
            var s = Encoding.Unicode.GetString(buf, off, NameBytes);
            int z = s.IndexOf('\0');
            names.Add(z >= 0 ? s[..z] : s);
        }
        return names.Where(n => n.Length > 0).ToArray();
    }
}
