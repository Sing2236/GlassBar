using System.IO;

namespace GlassBar.Services;

public static class StickerService
{
    private static readonly string StickerFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GlassBar", "Stickers");

    public static string Import(string sourcePath)
    {
        Directory.CreateDirectory(StickerFolder);
        var destination = Path.Combine(StickerFolder, $"{Guid.NewGuid():N}{Path.GetExtension(sourcePath)}");
        File.Copy(sourcePath, destination, overwrite: false);
        return destination;
    }
}
