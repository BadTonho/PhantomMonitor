using PhantomMonitor.Models;

namespace PhantomMonitor.Services.Linux;

/// <summary>Discovers device identities without requiring privileged commands.</summary>
internal sealed class LinuxHardwareIdentityReader
{
    private readonly string _procRoot;
    private readonly string _sysRoot;
    private readonly string[] _pciDatabases;

    public LinuxHardwareIdentityReader(string procRoot = "/proc", string sysRoot = "/sys", string[]? pciDatabases = null)
    {
        _procRoot = procRoot;
        _sysRoot = sysRoot;
        _pciDatabases = pciDatabases ?? ["/usr/share/hwdata/pci.ids", "/usr/share/misc/pci.ids", "/usr/share/pci.ids"];
    }

    public List<HardwareIdentity> Read()
    {
        var result = new List<HardwareIdentity>();
        ReadCpus(result);
        var board = ReadText(Path.Combine(_sysRoot, "class/dmi/id/board_name"));
        var vendor = ReadText(Path.Combine(_sysRoot, "class/dmi/id/board_vendor"));
        if (Useful(board)) result.Add(new HardwareIdentity("motherboard", "linux:board", Useful(vendor) ? $"{vendor} {board}" : board!));
        ReadGpus(result);
        return result;
    }

    private void ReadCpus(List<HardwareIdentity> result)
    {
        var contents = ReadText(Path.Combine(_procRoot, "cpuinfo"));
        if (contents is null) return;
        foreach (var section in contents.Replace("\r", "").Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var values = section.Split('\n').Select(line => line.Split(':', 2))
                .Where(parts => parts.Length == 2)
                .GroupBy(parts => parts[0].Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First()[1].Trim(), StringComparer.OrdinalIgnoreCase);
            if (!values.TryGetValue("model name", out var name) && !values.TryGetValue("Processor", out name)) continue;
            var id = values.TryGetValue("physical id", out var physical) ? physical : "0";
            if (Useful(name) && !result.Any(device => device.Category == "cpu" && device.Id == "linux:cpu:" + id))
                result.Add(new HardwareIdentity("cpu", "linux:cpu:" + id, name));
        }
    }

    private void ReadGpus(List<HardwareIdentity> result)
    {
        var root = Path.Combine(_sysRoot, "class/drm");
        string[] cards;
        try { cards = Directory.GetDirectories(root, "card*"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; }
        foreach (var card in cards.OrderBy(path => path, StringComparer.Ordinal))
        {
            var cardName = Path.GetFileName(card);
            if (cardName.Length <= 4 || !cardName[4..].All(char.IsDigit)) continue;
            var device = Path.Combine(card, "device");
            var vendor = ReadText(Path.Combine(device, "vendor"))?.Replace("0x", "", StringComparison.OrdinalIgnoreCase);
            var model = ReadText(Path.Combine(device, "device"))?.Replace("0x", "", StringComparison.OrdinalIgnoreCase);
            var uevent = ReadText(Path.Combine(device, "uevent"));
            var pci = uevent?.Split('\n').FirstOrDefault(line => line.StartsWith("PCI_SLOT_NAME=", StringComparison.Ordinal))?[14..].Trim();
            if (string.IsNullOrWhiteSpace(pci)) continue;
            var name = FindPciName(vendor, model) ?? "Modelo indisponível";
            result.Add(new HardwareIdentity("gpu", "linux:pci:" + pci, name));
        }
    }

    private string? FindPciName(string? vendor, string? model)
    {
        if (string.IsNullOrWhiteSpace(vendor) || string.IsNullOrWhiteSpace(model)) return null;
        foreach (var path in _pciDatabases)
        {
            try
            {
                if (!File.Exists(path)) continue;
                string? vendorName = null;
                foreach (var line in File.ReadLines(path))
                {
                    if (line.Length == 0 || line[0] == '#') continue;
                    if (line[0] != '\t')
                    {
                        vendorName = line.Length > 5 && line[..4].Equals(vendor, StringComparison.OrdinalIgnoreCase)
                            ? line[4..].Trim() : null;
                    }
                    else if (vendorName is not null && line.Length > 6 && line[1] != '\t'
                        && line.Substring(1, 4).Equals(model, StringComparison.OrdinalIgnoreCase))
                        return $"{vendorName} {line[5..].Trim()}";
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return null;
    }

    private static string? ReadText(string path)
    {
        try { return File.ReadAllText(path).Trim(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    private static bool Useful(string? value) => !string.IsNullOrWhiteSpace(value)
        && !value.Equals("unknown", StringComparison.OrdinalIgnoreCase)
        && !value.Equals("to be filled by o.e.m.", StringComparison.OrdinalIgnoreCase);
}
