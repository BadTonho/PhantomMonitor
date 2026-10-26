using PhantomMonitor.Models;

namespace PhantomMonitor.Services;

public static class OverlayCustomLineRenderer
{
    public static string Render(OverlayCustomLineConfig line, IReadOnlyList<HardwareIdentity> devices)
    {
        var text = line.Text ?? "";
        foreach (var reference in line.Hardware ?? [])
        {
            var device = devices.FirstOrDefault(item => item.Id == reference.DeviceId && item.Category == reference.Category);
            text = text.Replace("{" + reference.Token + "}", device?.Name ?? "Indisponível", StringComparison.Ordinal);
        }
        return text;
    }
}
