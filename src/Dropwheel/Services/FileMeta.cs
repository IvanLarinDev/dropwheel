using System.IO;

namespace Dropwheel.Services;

/// <summary>File properties a routing rule can test. Read once per item during a Plan. A folder has no
/// extension and no meaningful size (its size stays 0), but its ages are read from its own timestamps so
/// age rules still apply. AgeDays is days since last change; CreationAgeDays is days since creation.</summary>
public readonly record struct FileMeta(
    string Name, string Ext, double SizeMb, double AgeDays, bool IsDirectory, double CreationAgeDays)
{
    /// <summary>Reads size and ages from disk. A folder yields extension "" and size 0. A missing path
    /// yields size/age 0 while name and extension are still parsed, so name/extension rules keep
    /// working.</summary>
    public static FileMeta Read(string path)
    {
        var name = Path.GetFileName(path);
        var now = DateTime.Now;
        if (Directory.Exists(path))
        {
            var di = new DirectoryInfo(path);
            return new FileMeta(name, "", 0,
                (now - di.LastWriteTime).TotalDays, true, (now - di.CreationTime).TotalDays);
        }
        var ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        var fi = new FileInfo(path);
        if (!fi.Exists) return new FileMeta(name, ext, 0, 0, false, 0);
        double sizeMb = fi.Length / (1024.0 * 1024.0);
        return new FileMeta(name, ext, sizeMb,
            (now - fi.LastWriteTime).TotalDays, false, (now - fi.CreationTime).TotalDays);
    }
}
