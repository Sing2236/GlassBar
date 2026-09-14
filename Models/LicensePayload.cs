namespace GlassBar.Models;

internal sealed class LicensePayload
{
    public string Product { get; set; } = string.Empty;
    public string Plan { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string LicenseId { get; set; } = string.Empty;
    public DateTimeOffset IssuedAtUtc { get; set; }
}
