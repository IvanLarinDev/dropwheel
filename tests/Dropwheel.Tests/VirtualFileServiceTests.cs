using System.IO;
using System.Text;
using Dropwheel.Services;

namespace Dropwheel.Tests;

/// <summary>Verifies FILEGROUPDESCRIPTORW buffer parsing: valid names are read, while a broken or
/// hostile count (negative, huge) yields an empty result instead of a crash or a giant
/// pre-allocation.</summary>
public sealed class VirtualFileServiceTests
{
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
}
