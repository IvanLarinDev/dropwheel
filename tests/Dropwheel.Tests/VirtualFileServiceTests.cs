using System.IO;
using System.Text;
using Dropwheel.Services;

namespace Dropwheel.Tests;

/// <summary>Verifies FILEGROUPDESCRIPTORW buffer parsing: valid names are read, while a broken or
/// hostile count (negative, huge) yields an empty result instead of a crash or a giant
/// pre-allocation.</summary>
public sealed class VirtualFileServiceTests
{
    [Fact]
    public void Descriptor_parser_rejects_buffers_above_the_external_input_cap()
    {
        Assert.Empty(VirtualFileService.ParseDescriptorNames(
            new byte[VirtualFileService.MaxDescriptorBytes + 1]));
        Assert.True(VirtualFileService.MaxVirtualFileBytes < VirtualFileService.MaxVirtualBatchBytes);
        Assert.InRange(VirtualFileService.VirtualCopyBufferBytes, 1, 1024 * 1024);
    }

    [Fact]
    public void Failed_virtual_items_keep_their_pessimistic_batch_reservations()
    {
        var budget = new VirtualFileService.VirtualBatchBudget();

        Assert.Equal(VirtualFileService.MaxVirtualFileBytes, budget.Reserve());
        Assert.Equal(VirtualFileService.MaxVirtualFileBytes, budget.Reserve());
        Assert.Equal(VirtualFileService.MaxVirtualBatchBytes, budget.UsedBytes);
        Assert.Equal(0, budget.Reserve());
    }

    [Fact]
    public void Successful_virtual_items_refund_unused_reservation_bytes()
    {
        var budget = new VirtualFileService.VirtualBatchBudget();
        var allowance = budget.Reserve();

        budget.Complete(allowance, 1234);

        Assert.Equal(1234, budget.UsedBytes);
        Assert.Equal(VirtualFileService.MaxVirtualFileBytes, budget.Reserve());
    }

    [Fact]
    public async Task Virtual_format_probe_never_calls_the_provider_on_the_caller_thread()
    {
        using var enteredProvider = new ManualResetEventSlim();
        using var releaseProvider = new ManualResetEventSlim();
        var data = new BlockingDescriptorDataObject(enteredProvider, releaseProvider);

        var call = Task.Run(() => VirtualFileService.HasVirtualFiles(data));
        try
        {
            Assert.Same(call, await Task.WhenAny(call, Task.Delay(1000)));
            Assert.False(await call);
            Assert.True(enteredProvider.Wait(1000));
        }
        finally
        {
            releaseProvider.Set();
        }

        Assert.True(SpinWait.SpinUntil(() => VirtualFileService.HasVirtualFiles(data), 2000));
    }

    private const int EntrySize = 592, NameOffset = 72;

    /// <summary>Builds a descriptor buffer with the given count and file names.</summary>
    private static byte[] BuildBuffer(int count, params string[] names)
    {
        var buf = new byte[4 + names.Length * EntrySize];
        BitConverter.GetBytes(count).CopyTo(buf, 0);
        for (int i = 0; i < names.Length; i++)
        {
            var bytes = Encoding.Unicode.GetBytes(names[i]);
            bytes.CopyTo(buf, 4 + i * EntrySize + NameOffset);
        }
        return buf;
    }

    [Fact]
    public void Reads_names_from_a_valid_buffer()
    {
        var buf = BuildBuffer(2, "photo.jpg", "notes.txt");
        var names = VirtualFileService.ParseDescriptorNames(buf);
        Assert.Equal(new[] { "photo.jpg", "notes.txt" }, names);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    [InlineData(1_000_000)]
    public void Out_of_range_count_yields_empty_without_throwing(int count)
    {
        var buf = BuildBuffer(1, "photo.jpg");
        BitConverter.GetBytes(count).CopyTo(buf, 0); // overwrite count with a garbage value
        var names = VirtualFileService.ParseDescriptorNames(buf);
        Assert.Empty(names);
    }

    [Fact]
    public void Truncated_buffer_yields_empty()
    {
        Assert.Empty(VirtualFileService.ParseDescriptorNames(new byte[] { 1, 0 }));
    }

    [Fact]
    public void Temp_path_for_virtual_file_stays_next_to_destination_and_is_hidden_tmp()
    {
        var dest = Path.Combine(Path.GetTempPath(), "invoice.pdf");
        var tmp = VirtualFileService.TempPathFor(dest);

        Assert.Equal(Path.GetDirectoryName(dest), Path.GetDirectoryName(tmp));
        Assert.StartsWith(".invoice.pdf.", Path.GetFileName(tmp));
        Assert.EndsWith(".tmp", tmp);
    }

    private sealed class BlockingDescriptorDataObject(
        ManualResetEventSlim enteredProvider,
        ManualResetEventSlim releaseProvider) : System.Windows.IDataObject
    {
        public object? GetData(string format) =>
            format == "FileGroupDescriptorW"
                ? new MemoryStream(BuildBuffer(1, "attachment.txt"))
                : null;

        public object? GetData(Type format) => null;
        public object? GetData(string format, bool autoConvert) => GetData(format);

        public bool GetDataPresent(string format)
        {
            enteredProvider.Set();
            releaseProvider.Wait(2000);
            return format is "FileGroupDescriptorW" or "FileContents";
        }

        public bool GetDataPresent(Type format) => false;
        public bool GetDataPresent(string format, bool autoConvert) => GetDataPresent(format);
        public string[] GetFormats() => ["FileGroupDescriptorW", "FileContents"];
        public string[] GetFormats(bool autoConvert) => GetFormats();
        public void SetData(string format, object data) => throw new NotSupportedException();
        public void SetData(Type format, object data) => throw new NotSupportedException();
        public void SetData(string format, object data, bool autoConvert) => throw new NotSupportedException();
        public void SetData(object data) => throw new NotSupportedException();
    }
}
