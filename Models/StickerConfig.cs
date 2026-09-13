namespace GlassBar.Models;

public sealed class StickerConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string DisplayName { get; set; } = "Sticker";
    public string FilePath { get; set; } = "";
    public double X { get; set; } = 0.5;
    public double Y { get; set; } = 0.5;
    public double Size { get; set; } = 52;
    public double Opacity { get; set; } = 0.95;
}
