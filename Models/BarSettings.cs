namespace GlassBar.Models;

public sealed class BarSettings
{
    public bool HideNativeTaskbar { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public bool UseWindowsSearch { get; set; }
    public double Opacity { get; set; } = 0.82;
    public double EffectIntensity { get; set; } = 0.72;
    public string Effect { get; set; } = "Rain";
    public string Accent { get; set; } = "#7DD3FC";
    public double BarWidth { get; set; } = 980;
    public double BarHeight { get; set; } = 68;
    public double CornerRadius { get; set; } = 22;
    public CustomEffectConfig CustomEffect { get; set; } = new();
    public List<StickerConfig> Stickers { get; set; } = [];
}
