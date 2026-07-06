using System.IO;

namespace Dropwheel.Services;

/// <summary>File properties a routing rule can test. Read once per file during a Plan.</summary>
public readonly record struct FileMeta(string Name, string Ext, double SizeMb, double AgeDays)
{
    /// <summary>Reads size and age from disk. A missing file yields size/age 0 while name
    /// and extension are still parsed from the path, so name/extension rules keep working.</summary>
    public static FileMeta Read(string path)
    {
        var name = Path.GetFileName(path);
        var ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        var fi = new FileInfo(path);
        if (!fi.Exists) return new FileMeta(name, ext, 0, 0);
        double sizeMb = fi.Length / (1024.0 * 1024.0);
        double ageDays = (DateTime.Now - fi.LastWriteTime).TotalDays;
        return new FileMeta(name, ext, sizeMb, ageDays);
    }
}
