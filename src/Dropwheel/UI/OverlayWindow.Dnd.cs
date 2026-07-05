using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Dropwheel.Models;
using Dropwheel.Services;

namespace Dropwheel.UI;

public partial class OverlayWindow
{
    /// <summary>Приоритет: модификатор (Ctrl/Shift) → оверрайд цели → глобальная настройка.</summary>
    private static DropAction Resolve(TargetItem t, DragEventArgs e)
    {
        if (e.KeyStates.HasFlag(DragDropKeyStates.ControlKey)) return DropAction.Copy;
        if (e.KeyStates.HasFlag(DragDropKeyStates.ShiftKey))   return DropAction.Move;
        if (t.Override != DropAction.Inherit) return t.Override;
        return TargetStore.Config.GlobalAction;
    }

    private void OnBubbleDragOver(TargetItem t, Border badge, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop) || !t.IsFolder)
        { e.Effects = DragDropEffects.None; e.Handled = true; return; }
        var act = Resolve(t, e);
        e.Effects = act == DropAction.Move ? DragDropEffects.Move : DragDropEffects.Copy;
        ((TextBlock)badge.Child).Text = act == DropAction.Move ? "➜" : "⧉";
        badge.Background = act == DropAction.Move ? Brushes.Orange : Brushes.MediumSpringGreen;
        badge.Visibility = Visibility.Visible;
        e.Handled = true;
    }

    private void OnBubbleDrop(TargetItem t, Border badge, DragEventArgs e)
    {
        badge.Visibility = Visibility.Collapsed;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0) return;
        var act = Resolve(t, e);
        bool ok = FileOps.Execute(files, t.Path, act);
        ShowToast(ok
            ? $"{(act == DropAction.Move ? "➜ Перемещено" : "⧉ Скопировано")}: {files.Length} шт. → {t.Name}"
            : "Операция не выполнена");
        CloseCloud();
        e.Handled = true;
    }
}
