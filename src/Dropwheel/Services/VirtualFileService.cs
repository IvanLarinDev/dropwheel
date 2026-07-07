using System.IO;
using System.Text;
using System.Windows;

namespace Dropwheel.Services;

/// <summary>Accepts "virtual" files (Outlook attachments, browser images):
/// CFSTR_FILEDESCRIPTORW + CFSTR_FILECONTENTS formats. Copy is the only possible action.</summary>
public static partial class VirtualFileService
{
    private const string DescriptorFormat = "FileGroupDescriptorW";
    private const string ContentsFormat = "FileContents";

    public static bool HasVirtualFiles(IDataObject data)
        => data.GetDataPresent(DescriptorFormat) && data.GetDataPresent(ContentsFormat);

    /// <summary>Saves all virtual files into a folder, returns the created file paths.</summary>
    public static string[] Extract(IDataObject data, string destFolder)
    {
        var names = ReadNames(data);
        if (names.Length == 0) return Array.Empty<string>();
        if (data is not System.Runtime.InteropServices.ComTypes.IDataObject com)
            return Array.Empty<string>();
        var saved = new List<string>();
        for (int i = 0; i < names.Length; i++)
        {
            try
            {
                var path = UniquePath(destFolder, names[i]);
                if (SaveContents(com, i, path)) saved.Add(path);
            }
            catch { /* one broken item must not fail the whole drop */ }
        }
        return saved.ToArray();
    }

    private static string[] ReadNames(IDataObject data)
    {
        if (data.GetData(DescriptorFormat) is not MemoryStream ms) return Array.Empty<string>();
        return ParseDescriptorNames(ms.ToArray());
    }

    // FILEGROUPDESCRIPTORW: UINT cItems; FILEDESCRIPTORW[cItems] (592 bytes each,
    // cFileName is WCHAR[260] at offset 72).
    /// <summary>Extracts the file names from a raw FILEGROUPDESCRIPTORW buffer. Pure so it can be
    /// tested directly. The item count comes from an external drag source, so an out-of-range value
    /// (corrupt data) yields an empty result instead of throwing or over-allocating.</summary>
    internal static string[] ParseDescriptorNames(byte[] buf)
    {
        if (buf.Length < 4) return Array.Empty<string>();
        int count = BitConverter.ToInt32(buf, 0);
        if (count <= 0 || count > 4096) return Array.Empty<string>();
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
