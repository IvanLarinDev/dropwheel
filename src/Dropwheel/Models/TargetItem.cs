using System.IO;

namespace Dropwheel.Models;

public enum DropAction { Inherit, Copy, Move }

public class TargetItem
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public DropAction Override { get; set; } = DropAction.Inherit;
    public bool Pinned { get; set; }

    public bool IsFolder => Directory.Exists(Path);
    public bool Exists => IsFolder || File.Exists(Path);
}
