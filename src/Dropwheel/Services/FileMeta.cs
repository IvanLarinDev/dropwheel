using System.IO;

namespace Dropwheel.Services;

/// <summary>File properties a routing rule can test. Read once per item during a Plan. A folder has no
/// extension and no meaningful size (its size stays 0), but its age is read from its own last-write
/// time so age rules still apply.</summary>
public readonly record struct FileMeta(string Name, string Ext, double SizeMb, double AgeDays, bool IsDirectory)
{
    /// <summary>Reads size and age from disk. A folder yields extension "" and size 0. A missing path
    /// yields size/age 0 while name and extension are still parsed, so name/extension rules keep
    /// working.</summary>
    public static FileMeta Read(string path)
    {
        var name = Path.GetFileName(path);
        if (Directory.Exists(path))
        {
            var di = new DirectoryInfo(path);
            double folderAge = (DateTime.Now - di.LastWriteTime).TotalDays;
            return new FileMeta(name, "", 0, folderAge, true);
        }
        var ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        var fi = new FileInfo(path);
        if (!fi.Exists) return new FileMeta(name, ext, 0, 0, false);
        double sizeMb = fi.Length / (1024.0 * 1024.0);
        double ageDays = (DateTime.Now - fi.LastWriteTime).TotalDays;
        return new FileMeta(name, ext, sizeMb, ageDays, false);
    }
}
