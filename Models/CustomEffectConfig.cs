namespace GlassBar.Models;

public sealed class CustomEffectConfig
{
    public string Name { get; set; } = "My effect";
    public string Shape { get; set; } = "Orb";
    public string Motion { get; set; } = "Float";
    public string SecondaryColor { get; set; } = "#A78BFA";
    public double Density { get; set; } = 0.6;
    public double Speed { get; set; } = 0.5;
    public double Size { get; set; } = 0.5;
    public double Glow { get; set; } = 0.55;
    public double Trail { get; set; } = 0.35;
}
