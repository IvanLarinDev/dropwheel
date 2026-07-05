using System.IO;
using System.Text.Json.Serialization;

namespace Dropwheel.Models;

public enum DropAction { Inherit, Copy, Move }

public class TargetItem
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public DropAction Override { get; set; } = DropAction.Inherit;
    public bool Pinned { get; set; }

    /// <summary>null — обычная цель; иначе это группа (одна степень вложенности).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<TargetItem>? Children { get; set; }

    /// <summary>Правила сортировки: ключ — расширения через пробел («jpg png») или «*»,
    /// значение — подпапка относительно Path или абсолютный путь. null — обычная цель.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? SortRules { get; set; }

    [JsonIgnore] public bool IsGroup => Children != null;
    [JsonIgnore] public bool IsSorter => SortRules is { Count: > 0 };
    [JsonIgnore] public bool IsFolder => !IsGroup && Directory.Exists(Path);
    [JsonIgnore] public bool Exists => IsGroup || IsFolder || File.Exists(Path);
}
