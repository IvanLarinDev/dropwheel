using System.IO;
using System.Windows.Input;
using Dropwheel.Models;
using Dropwheel.Services;
using IOPath = System.IO.Path;

namespace Dropwheel.UI;

public partial class OverlayWindow
{
    // One drop operation may consist of several moves (sorter).
    private readonly List<(DropAction Act, string[] Sources, string Dest)> _lastOps = new();

    private void RememberOp(DropAction act, string[] sources, string dest)
    { _lastOps.Clear(); _lastOps.Add((act, sources, dest)); }

    private void RememberOps(IEnumerable<(DropAction, string[], string)> ops)
    { _lastOps.Clear(); _lastOps.AddRange(ops); }

    private void OnUndoClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        Undo();
    }

    /// <summary>Best-effort: copy → delete the copies (to Recycle Bin), move → move back.
    /// Files renamed by the conflict dialog are not tracked.</summary>
    private void Undo()
    {
        if (_lastOps.Count == 0) return;
        bool ok = true;
        foreach (var op in _lastOps) ok &= UndoOne(op);
        _lastOps.Clear();
        ShowToast(ok ? "↩ Undone" : "Could not undo completely");
    }

    private static bool UndoOne((DropAction Act, string[] Sources, string Dest) op)
    {
        bool ok = true;
        if (op.Act == DropAction.Copy)
        {
            var copies = op.Sources
                .Select(s => IOPath.Combine(op.Dest, IOPath.GetFileName(s)))
                .Where(p => File.Exists(p) || Directory.Exists(p)).ToArray();
            if (copies.Length > 0) ok = FileOps.Delete(copies);
        }
        else
        {
            foreach (var src in op.Sources)
            {
                var dst = IOPath.Combine(op.Dest, IOPath.GetFileName(src));
                if ((File.Exists(dst) || Directory.Exists(dst))
                    && IOPath.GetDirectoryName(src) is { Length: > 0 } dir)
                    ok &= FileOps.Execute(new[] { dst }, dir, DropAction.Move);
            }
        }
        return ok;
    }
}
