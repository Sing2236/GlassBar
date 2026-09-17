namespace GlassBar.Models;

public sealed class BarSettings
{
    public bool HideNativeTaskbar { get; set; } = true;
    public bool HideInFullscreenApps { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public bool TopOverlayEnabled { get; set; }
    public bool TopShowClock { get; set; } = true;
    public bool TopShowPerformance { get; set; } = true;
    public bool TopShowConnection { get; set; } = true;
    public bool TopShowPower { get; set; } = true;
    public bool TopShowFocus { get; set; } = true;
    public bool TopShowNews { get; set; } = true;
    public int TopFocusMinutes { get; set; } = 25;
    public string BarOrientation { get; set; } = "Horizontal";
    public double BarX { get; set; } = -1;
    public double BarY { get; set; } = -1;
    public string TopBarOrientation { get; set; } = "Horizontal";
    public double TopBarX { get; set; } = -1;
    public double TopBarY { get; set; } = -1;
    public bool AudioVisualizerEnabled { get; set; }
    public double AudioVisualizerX { get; set; } = -1;
    public double AudioVisualizerY { get; set; } = -1;
    public bool UseWindowsSearch { get; set; }
    public bool AltTabEnabled { get; set; } = true;
    public string AltTabBackground { get; set; } = "Glass";
    public double AltTabOpacity { get; set; } = 0.92;
    public bool BackgroundVisible { get; set; } = true;
    public double Opacity { get; set; } = 0.82;
    public double EffectIntensity { get; set; } = 0.72;
    public string Effect { get; set; } = "Rain";
    public string Accent { get; set; } = "#7DD3FC";
    public string BackgroundStart { get; set; } = "#111722";
    public string BackgroundEnd { get; set; } = "#111722";
    public string Border { get; set; } = "#FFFFFF";
    public double Shadow { get; set; } = 36;
    public double BarWidth { get; set; } = 980;
    public double BarHeight { get; set; } = 68;
    public double TopBarWidth { get; set; }
    public double TopBarHeight { get; set; } = 54;
    public double CornerRadius { get; set; } = 22;
    public CustomEffectConfig CustomEffect { get; set; } = new();
    public bool CommunityAnimationActive { get; set; }
    public List<StickerConfig> Stickers { get; set; } = [];
    public List<StickerConfig> TopStickers { get; set; } = [];
    public List<PinnedAppConfig> PinnedApps { get; set; } = [];
}
