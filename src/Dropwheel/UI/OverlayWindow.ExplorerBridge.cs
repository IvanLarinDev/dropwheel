using System.IO;
using System.Windows;
using Dropwheel.Models;
using Dropwheel.Services;

namespace Dropwheel.UI;

public partial class OverlayWindow
{
    private string[]? _explorerBridgeFiles;

    public void OpenFromExplorerFiles(IEnumerable<string> paths)
    {
        var files = paths
            .Where(path => File.Exists(path) || Directory.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (files.Length == 0)
        {
            ShowToast("Explorer did not pass any existing files", kind: ToastKind.Warning);
            return;
        }

        if (!IsVisible) Show();
        UpdateOrbScreenPos();
        _ignoreDeactivateUntilUtc = DateTime.UtcNow.AddMilliseconds(750);

        if (ShouldAutoAddExplorerTargets(files))
        {
            AddExplorerBridgeTargets(files, "Added automatically from Explorer SendTo.");
            _explorerBridgeFiles = null;
            if (!_open) OpenCloud();
            return;
        }

        _explorerBridgeFiles = files;
        OpenCloud();
        ShowToast($"Explorer selection: {files.Length} item(s). Choose a target or click Add.", kind: ToastKind.Info);
    }

    private async Task<bool> TryHandleExplorerBridgeTargetAsync(TargetItem target)
    {
        if (_explorerBridgeFiles is not { Length: > 0 } files) return false;

        if (target.IsGroup)
        {
            EnterGroup(target);
            return true;
        }

        if (!target.Exists)
        {
            ShowMissingMenu(target);
            return true;
        }

        if (await DropExplorerFilesAsync(target, files))
        {
            _explorerBridgeFiles = null;
            CloseCloud();
        }
        return true;
    }

    private bool TryAddExplorerBridgeTargets()
    {
        if (_explorerBridgeFiles is not { Length: > 0 } files) return false;

        AddExplorerBridgeTargets(files, "Added from Explorer SendTo.");
        _explorerBridgeFiles = null;
        return true;
    }

    private void AddExplorerBridgeTargets(IReadOnlyList<string> files, string detail)
    {
        var targets = files.Select(TargetFromPath).ToArray();
        var added = AddTargets(targets, _currentGroup, rememberHistory: false);
        if (added.Length == 0) return;
        RememberDropHistory(
            DropHistoryAction.AddTargets,
            new TargetItem { Name = _currentGroup?.Name ?? "Wheel", Path = "" },
            DropPayloadKind.Files,
            added.Length,
            DropHistoryStatus.Succeeded,
            destination: added.Length == 1 ? added[0].Path : null,
            detail: detail);
    }

    internal static bool ShouldAutoAddExplorerTargets(IReadOnlyList<string> files) =>
        files.Count > 0 && files.All(IsExplorerTargetLike);

    private static bool IsExplorerTargetLike(string path)
    {
        if (Directory.Exists(path)) return true;
        if (!File.Exists(path)) return false;

        var extension = Path.GetExtension(path);
        return extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase)
            || TargetItem.IsExeExtension(path);
    }

    private async Task<bool> DropExplorerFilesAsync(TargetItem target, string[] files)
    {
        var plan = DropExecutionService.PlanRealFiles(
            target,
            ctrl: false,
            shift: false,
            TargetStore.Config.GlobalAction,
            DropDispatch.SortingPaused);
        if (plan.Route == RealFileDropRoute.Denied)
        {
            ShowToast(plan.DenialReason ?? "Target cannot receive this drop.", kind: ToastKind.Warning);
            return false;
        }

        if (!ConfirmDropPreflight(target, DropPayloadKind.Files, plan.TargetKind, files.Length))
            return false;

        await ExecuteRealFileDropAsync(target, files, plan, fromExplorer: true);
        return true;
    }
}
