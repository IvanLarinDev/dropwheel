using System.Runtime.InteropServices;

namespace Dropwheel.Services;

/// <summary>Определяет, что на переднем плане полноэкранное приложение/игра/презентация —
/// в этом случае оверлей прячется, чтобы не мешать.</summary>
public static class FullscreenDetector
{
    private enum Quns
    {
        NotPresent = 1, Busy, RunningD3dFullScreen, PresentationMode,
        AcceptsNotifications, QuietTime, App
    }

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out Quns state);

    public static bool IsFullscreenActive()
        => SHQueryUserNotificationState(out var s) == 0
           && s is Quns.Busy or Quns.RunningD3dFullScreen or Quns.PresentationMode;
}
