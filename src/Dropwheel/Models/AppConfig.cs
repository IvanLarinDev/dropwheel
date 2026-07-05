namespace Dropwheel.Models;

public class AppConfig
{
    public DropAction GlobalAction { get; set; } = DropAction.Copy;

    // Центр кружка в экранных координатах; -1 = по умолчанию (правый край, середина).
    public double OrbX { get; set; } = -1;
    public double OrbY { get; set; } = -1;

    public double OrbOpacity { get; set; } = 0.8;
    public int HoverDelayMs { get; set; } = 250;

    public List<TargetItem> Targets { get; set; } = new();
}
