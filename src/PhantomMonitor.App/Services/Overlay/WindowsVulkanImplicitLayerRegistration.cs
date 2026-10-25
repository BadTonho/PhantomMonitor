using Microsoft.Win32;
using System.Runtime.Versioning;

namespace PhantomMonitor.App.Services.Overlay;

internal sealed record VulkanLayerRegistryEntry(RegistryView View, string ManifestPath);

internal interface IVulkanLayerRegistryWriter
{
    void Register(VulkanLayerRegistryEntry entry);
    void Remove(VulkanLayerRegistryEntry entry);
    IReadOnlyList<VulkanLayerRegistryEntry> FindOwnedEntries();
}

[SupportedOSPlatform("windows")]
internal sealed class WindowsVulkanImplicitLayerRegistration
{
    internal const string RegistrySubKey = @"SOFTWARE\Khronos\Vulkan\ImplicitLayers";
    private readonly IVulkanLayerRegistryWriter _registry;

    internal WindowsVulkanImplicitLayerRegistration(IVulkanLayerRegistryWriter? registry = null)
        => _registry = registry ?? new WindowsVulkanLayerRegistryWriter();

    internal IReadOnlyList<VulkanLayerRegistryEntry> GetEntries(string appBaseDirectory)
    {
        var entries = new List<VulkanLayerRegistryEntry>
        {
            new(RegistryView.Registry64, Path.GetFullPath(Path.Combine(appBaseDirectory,
                "PhantomOverlay", "x64", "phantom_vulkan_layer.json"))),
            new(RegistryView.Registry32, Path.GetFullPath(Path.Combine(appBaseDirectory,
                "PhantomOverlay", "x86", "phantom_vulkan_layer.json")))
        };
        return Environment.Is64BitOperatingSystem ? entries : [entries[1]];
    }

    internal string Enable(string appBaseDirectory)
    {
        var entries = GetEntries(appBaseDirectory);
        var installed = entries.Where(entry => File.Exists(entry.ManifestPath)).ToArray();
        if (installed.Length == 0)
            return "Camadas Vulkan x86/x64 não encontradas em PhantomOverlay; Direct3D/OpenGL continuam disponíveis.";

        var completed = new List<VulkanLayerRegistryEntry>();
        try
        {
            foreach (var entry in installed)
            {
                _registry.Register(entry);
                completed.Add(entry);
            }
            var missing = entries.Except(installed).Any()
                ? " A variante Vulkan de uma arquitetura não está compilada neste pacote."
                : "";
            return $"Camada Vulkan implícita ativa para o usuário ({string.Join("/", installed.Select(entry => entry.View == RegistryView.Registry32 ? "x86" : "x64"))}).{missing}";
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or ArgumentException
                                   or System.Security.SecurityException)
        {
            foreach (var entry in completed)
            {
                try { _registry.Remove(entry); }
                catch { }
            }
            return $"Falha ao registrar a camada Vulkan no perfil do usuário: {ex.Message}";
        }
    }

    internal string Disable(string appBaseDirectory)
    {
        var errors = new List<string>();
        var entries = GetEntries(appBaseDirectory).ToList();
        try { entries.AddRange(_registry.FindOwnedEntries()); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or ArgumentException
                                   or System.Security.SecurityException)
        {
            errors.Add($"não foi possível localizar registros antigos: {ex.Message}");
        }
        foreach (var entry in entries.DistinctBy(entry => (entry.View, entry.ManifestPath)))
        {
            try { _registry.Remove(entry); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or ArgumentException
                                       or System.Security.SecurityException)
            {
                errors.Add(ex.Message);
            }
        }
        return errors.Count == 0
            ? "Camada Vulkan removida do perfil do usuário."
            : $"Falha ao remover a camada Vulkan do perfil do usuário: {string.Join("; ", errors)}";
    }
}

[SupportedOSPlatform("windows")]
internal sealed class WindowsVulkanLayerRegistryWriter : IVulkanLayerRegistryWriter
{
    public void Register(VulkanLayerRegistryEntry entry)
    {
        using var root = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, entry.View);
        using var key = root.CreateSubKey(WindowsVulkanImplicitLayerRegistration.RegistrySubKey, writable: true)
            ?? throw new IOException("Não foi possível criar a chave de camadas Vulkan do usuário.");
        key.SetValue(entry.ManifestPath, 0, RegistryValueKind.DWord);
    }

    public void Remove(VulkanLayerRegistryEntry entry)
    {
        using var root = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, entry.View);
        using var key = root.OpenSubKey(WindowsVulkanImplicitLayerRegistration.RegistrySubKey, writable: true);
        key?.DeleteValue(entry.ManifestPath, throwOnMissingValue: false);
    }

    public IReadOnlyList<VulkanLayerRegistryEntry> FindOwnedEntries()
    {
        var views = Environment.Is64BitOperatingSystem
            ? new[] { RegistryView.Registry64, RegistryView.Registry32 }
            : [RegistryView.Registry32];
        var entries = new List<VulkanLayerRegistryEntry>();
        foreach (var view in views)
        {
            using var root = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, view);
            using var key = root.OpenSubKey(WindowsVulkanImplicitLayerRegistration.RegistrySubKey);
            if (key is null) continue;
            foreach (var valueName in key.GetValueNames())
            {
                if (IsOwnedManifestPath(valueName, view))
                    entries.Add(new VulkanLayerRegistryEntry(view, valueName));
            }
        }
        return entries;
    }

    private static bool IsOwnedManifestPath(string path, RegistryView view)
    {
        var architecture = view == RegistryView.Registry32 ? "x86" : "x64";
        var suffix = $"{Path.DirectorySeparatorChar}PhantomOverlay{Path.DirectorySeparatorChar}{architecture}{Path.DirectorySeparatorChar}phantom_vulkan_layer.json";
        return Path.IsPathFullyQualified(path)
            && path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
    }
}
