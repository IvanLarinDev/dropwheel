using System.IO;
using System.Windows.Input;
using Dropwheel.Models;
using Dropwheel.Services;
using IOPath = System.IO.Path;

namespace Dropwheel.UI;

public partial class OverlayWindow
{
    private (DropAction Act, string[] Sources, string Dest)? _lastOp;

    private void RememberOp(DropAction act, string[] sources, string dest)
        => _lastOp = (act, sources, dest);

    private void OnUndoClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        Undo();
    }

    /// <summary>Best-effort: copy → удалить копии (в Корзину), move → вернуть на место.
    /// Файлы, переименованные конфликт-диалогом, не отслеживаем.</summary>
    private void Undo()
    {
        if (_lastOp is not { } op) return;
        _lastOp = null;
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
        ShowToast(ok ? "↩ Undone" : "Could not undo completely");
    }
}
