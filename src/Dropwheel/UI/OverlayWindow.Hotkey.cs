using System.Windows;
using System.Windows.Threading;
using Dropwheel.Services;
using WF = System.Windows.Forms;

namespace Dropwheel.UI;

public partial class OverlayWindow
{
    private HotkeyService? _hotkey;
    private string _hotkeyActive = ""; // последняя успешно зарегистрированная комбинация
    private DispatcherTimer? _fsTimer;
    private bool _hiddenByFullscreen;

    private void InitHotkeyAndFullscreen()
    {
        // При старте прежней рабочей комбинации ещё нет: если конфиг занят другим
        // приложением, просто работаем без хоткея и молча оставляем след в логе.
        ApplyHotkey(TargetStore.Config.Hotkey, notify: false);

        _fsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _fsTimer.Tick += (_, _) =>
        {
            bool fs = FullscreenDetector.IsFullscreenActive();
            if (fs && !_hiddenByFullscreen) { _hiddenByFullscreen = true; CloseCloud(); Hide(); }
            else if (!fs && _hiddenByFullscreen) { _hiddenByFullscreen = false; Show(); }
        };
        _fsTimer.Start();

        Closed += (_, _) => { _hotkey?.Dispose(); _fsTimer?.Stop(); };
    }

    /// <summary>Hotkey: open the wheel at the cursor; press again to close and send the orb home.</summary>
    private void OnHotkey()
    {
        if (!IsVisible) return; // hidden by fullscreen
        if (_open)
        {
            CloseCloud();
            PlaceWindow(); // back home
            return;
        }
        var c = WF.Cursor.Position; // device px
        if (PresentationSource.FromVisual(this)?.CompositionTarget is { } ct)
        {
            var dip = ct.TransformFromDevice.Transform(new Point(c.X, c.Y));
            Left = dip.X - HalfSize;
            Top  = dip.Y - HalfSize;
        }
        UpdateOrbScreenPos();
        OpenCloud();
    }

    /// <summary>Регистрирует горячую клавишу из конфига. Если комбинация занята другим процессом,
    /// возвращается к последней рабочей комбинации и откатывает конфиг — чтобы неверный ввод в
    /// настройках не оставил приложение вообще без хоткея. Сначала освобождаем старую регистрацию
    /// (иначе тот же id занят), затем пробуем новую; при неудаче перерегистрируем прежнюю.</summary>
    private void ApplyHotkey(string hotkey, bool notify)
    {
        _hotkey?.Dispose();
        _hotkey = null;
        try
        {
            _hotkey = new HotkeyService(this, hotkey, OnHotkey);
            _hotkeyActive = hotkey;
            return;
        }
        catch (Exception ex)
        {
            ErrorLog.Write($"Не удалось зарегистрировать хоткей «{hotkey}»", ex);
        }

        bool rolledBack = _hotkeyActive.Length > 0 && _hotkeyActive != hotkey;
        if (rolledBack)
        {
            TargetStore.Config.Hotkey = _hotkeyActive;
            TargetStore.Save();
            try { _hotkey = new HotkeyService(this, _hotkeyActive, OnHotkey); }
            catch (Exception ex) { ErrorLog.Write($"Не удалось вернуть прежний хоткей «{_hotkeyActive}»", ex); }
        }
        if (notify)
            ShowToast(rolledBack
                ? $"Горячая клавиша {hotkey} занята — оставлена прежняя {_hotkeyActive}"
                : $"Горячая клавиша {hotkey} занята другим приложением");
    }
}
