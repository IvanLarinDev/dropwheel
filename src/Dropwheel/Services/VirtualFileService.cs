using System.IO;
using System.Text;
using System.Windows;

namespace Dropwheel.Services;

/// <summary>Приём «виртуальных» файлов (вложения Outlook, картинки из браузера):
/// форматы CFSTR_FILEDESCRIPTORW + CFSTR_FILECONTENTS. Для них возможна только копия.</summary>
public static partial class VirtualFileService
{
    private const string DescriptorFormat = "FileGroupDescriptorW";
    private const string ContentsFormat = "FileContents";

    public static bool HasVirtualFiles(IDataObject data)
        => data.GetDataPresent(DescriptorFormat) && data.GetDataPresent(ContentsFormat);

    /// <summary>Сохраняет все виртуальные файлы в папку, возвращает пути созданных файлов.</summary>
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
            catch { /* один битый элемент не должен валить весь бросок */ }
        }
        return saved.ToArray();
    }

    // FILEGROUPDESCRIPTORW: UINT cItems; FILEDESCRIPTORW[cItems] (592 байта каждый,
    // cFileName — WCHAR[260] со смещением 72).
    private static string[] ReadNames(IDataObject data)
    {
        if (data.GetData(DescriptorFormat) is not MemoryStream ms) return Array.Empty<string>();
        var buf = ms.ToArray();
        if (buf.Length < 4) return Array.Empty<string>();
        int count = BitConverter.ToInt32(buf, 0);
        const int EntrySize = 592, NameOffset = 72, NameBytes = 520;
        var names = new List<string>(count);
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
