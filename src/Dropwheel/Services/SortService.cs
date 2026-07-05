using System.IO;
using Dropwheel.Models;

namespace Dropwheel.Services;

/// <summary>Раскладка файлов по правилам цели-сортировщика.</summary>
public static class SortService
{
    /// <summary>Возвращает план: папка-назначение → файлы. Без совпадений и без «*» —
    /// файл идёт в корень цели (t.Path).</summary>
    public static Dictionary<string, List<string>> Plan(TargetItem t, IEnumerable<string> files)
    {
        var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in files)
        {
            var folder = ResolveFolder(t, f);
            if (!map.TryGetValue(folder, out var list)) map[folder] = list = new();
            list.Add(f);
        }
        return map;
    }

    private static string ResolveFolder(TargetItem t, string file)
    {
        var ext = Path.GetExtension(file).TrimStart('.').ToLowerInvariant();
        string? dest = null, fallback = null;
        foreach (var (key, value) in t.SortRules!)
        {
            if (key.Trim() == "*") { fallback = value; continue; }
            var exts = key.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                          .Select(x => x.TrimStart('.').ToLowerInvariant());
            if (ext.Length > 0 && exts.Contains(ext)) { dest = value; break; }
        }
        dest ??= fallback;
        if (dest == null) return t.Path;
        return Path.IsPathRooted(dest) ? dest : Path.Combine(t.Path, dest);
    }
}
