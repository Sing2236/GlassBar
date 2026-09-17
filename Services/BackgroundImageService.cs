using System.IO;

namespace GlassBar.Services;

public static class BackgroundImageService
{
    private static readonly string BackgroundFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GlassBar", "Backgrounds");

    public static string Import(string sourcePath)
    {
        Directory.CreateDirectory(BackgroundFolder);
        var destination = Path.Combine(BackgroundFolder, $"{Guid.NewGuid():N}{Path.GetExtension(sourcePath)}");
        File.Copy(sourcePath, destination, overwrite: false);
        return destination;
    }
}
