namespace Dropwheel.Models;

public class AppConfig
{
    public DropAction GlobalAction { get; set; } = DropAction.Copy;

    // Центр кружка (DIP, виртуальный экран); NaN = по умолчанию (правый край primary).
    // -1 не годится как маркер: монитор слева от основного даёт отрицательные координаты.
    public double OrbX { get; set; } = double.NaN;
    public double OrbY { get; set; } = double.NaN;

    public double OrbOpacity { get; set; } = 0.8;
    public int HoverDelayMs { get; set; } = 250;
    public string Hotkey { get; set; } = "Ctrl+Alt+Space";

    /// <summary>Через сколько секунд бездействия пригасить кружок (0 = выключено).</summary>
    public int IdleFadeSeconds { get; set; } = 0;

    public List<TargetItem> Targets { get; set; } = new();
}
