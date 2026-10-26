using PhantomMonitor.Models;

namespace PhantomMonitor.App.ViewModels;

public sealed record OverlayHardwareOption(HardwareIdentity Device, string Label);

/// <summary>Only explicit choices return a device; inventory refreshes never insert text.</summary>
internal sealed class OverlayHardwareSelection
{
    public string? Category { get; private set; }
    public IReadOnlyList<OverlayHardwareOption> Options { get; private set; } = [];
    public string Message { get; private set; } = "";

    public HardwareIdentity? ChooseCategory(string category, IReadOnlyList<HardwareIdentity> devices)
    {
        Category = category;
        Options = BuildOptions(category, devices);
        if (Options.Count == 1)
        {
            var selected = Options[0].Device;
            Reset($"Inserido: {selected.Name}");
            return selected;
        }
        if (Options.Count == 0)
        {
            Reset($"Nenhum dispositivo de {category} foi detectado. Inicie o monitoramento e tente novamente.");
            return null;
        }
        Message = "Selecione o dispositivo desejado.";
        return null;
    }

    public HardwareIdentity? ChooseDevice(OverlayHardwareOption option)
    {
        if (Category is null || Options.Count < 2) return null;
        var selected = Options.FirstOrDefault(candidate => candidate.Device.Id == option.Device.Id
            && candidate.Device.Category == option.Device.Category)?.Device;
        if (selected is null) return null;
        Reset($"Inserido: {selected.Name}");
        return selected;
    }

    public void Refresh(IReadOnlyList<HardwareIdentity> devices)
    {
        if (Category is null) return;
        Options = BuildOptions(Category, devices);
        if (Options.Count < 2)
            Reset("Os dispositivos mudaram. Selecione a categoria novamente.");
    }

    public void Reset(string message = "")
    {
        Category = null;
        Options = [];
        Message = message;
    }

    private static IReadOnlyList<OverlayHardwareOption> BuildOptions(string category, IReadOnlyList<HardwareIdentity> devices)
    {
        var key = category switch { "GPU" => "gpu", "CPU" => "cpu", "Placa-mãe" => "motherboard", _ => "" };
        return devices.Where(device => device.Category == key)
            .OrderBy(device => device.Id, StringComparer.Ordinal)
            .Select((device, index) => new OverlayHardwareOption(device, $"{category} {index + 1} · {device.Name}"))
            .ToArray();
    }
}
