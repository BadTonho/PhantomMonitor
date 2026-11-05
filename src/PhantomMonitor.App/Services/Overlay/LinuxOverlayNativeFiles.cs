using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PhantomMonitor.Services;

namespace PhantomMonitor.App.Services.Overlay;

/// <summary>Locates the Linux renderers bundled with the app and registers them for this user.</summary>
internal static class LinuxOverlayNativeFiles
{
    private const string VulkanLayerFileName = "libVkLayer_phantom_monitor.so";
    private const string OpenGlLibraryFileName = "libPhantomOpenGLOverlay.so";
    private const string ManifestFileName = "phantom_vulkan_layer.json";
    private const string HudExecutableFileName = "phantom-hud";

    private static string HomeDirectory => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private static string BundledDirectory => Path.Combine(AppContext.BaseDirectory, "PhantomOverlay", "linux");
    private static string UserLibraryDirectory => Path.Combine(HomeDirectory, ".local", "lib");
    private static string UserBinDirectory => Path.Combine(HomeDirectory, ".local", "bin");
    private static string UserImplicitLayerDirectory => Path.Combine(HomeDirectory, ".local", "share", "vulkan", "implicit_layer.d");

    private static string BundledVulkanLibrary => Path.Combine(BundledDirectory, VulkanLayerFileName);
    private static string BundledOpenGlLibrary => Path.Combine(BundledDirectory, OpenGlLibraryFileName);
    private static string BundledManifest => Path.Combine(BundledDirectory, ManifestFileName);
    private static string BundledHudExecutable => Path.Combine(BundledDirectory, HudExecutableFileName);

    public static string InstalledVulkanLibrary => Path.Combine(UserLibraryDirectory, VulkanLayerFileName);
    public static string InstalledOpenGlLibrary => Path.Combine(UserLibraryDirectory, OpenGlLibraryFileName);
    public static string InstalledManifest => Path.Combine(UserImplicitLayerDirectory, ManifestFileName);
    public static string InstalledHudExecutable => Path.Combine(UserBinDirectory, HudExecutableFileName);

    public static bool HasBundledPayload => File.Exists(BundledVulkanLibrary)
        && File.Exists(BundledOpenGlLibrary)
        && File.Exists(BundledManifest);

    public static bool HasInstalledVulkanLayer => File.Exists(InstalledVulkanLibrary)
        && File.Exists(InstalledManifest);

    public static bool HasInstalledOpenGlPreload => File.Exists(InstalledOpenGlLibrary);

    public static bool HasInstalledHudExecutable => File.Exists(InstalledHudExecutable);

    /// <summary>Removes this user's Vulkan registration while leaving the libraries intact.</summary>
    public static void RemoveInstalledRegistration()
    {
        if (!OperatingSystem.IsLinux() || !File.Exists(InstalledManifest)) return;

        File.Delete(InstalledManifest);
        if (Directory.Exists(UserImplicitLayerDirectory)
            && !Directory.EnumerateFileSystemEntries(UserImplicitLayerDirectory).Any())
        {
            Directory.Delete(UserImplicitLayerDirectory);
        }
    }

    /// <summary>
    /// Copies the packaged native libraries into the user's Vulkan/OpenGL locations.
    /// No compiler or administrator permission is needed on the user's machine.
    /// </summary>
    public static bool EnsureInstalled(out string message)
    {
        if (!OperatingSystem.IsLinux())
        {
            message = "A instalação dos renderizadores Linux não se aplica a esta plataforma.";
            return false;
        }

        if (!HasBundledPayload)
        {
            if (HasInstalledVulkanLayer && HasInstalledOpenGlPreload)
            {
                EnsureHudExecutableInstalled();
                message = "Usando os renderizadores Linux já instalados para este usuário e comando phantom-hud configurado.";
                return true;
            }

            var missing = new List<string>();
            if (!HasInstalledVulkanLayer) missing.Add("camada Vulkan");
            if (!HasInstalledOpenGlPreload) missing.Add("preload OpenGL");
            message = $"Esta distribuição não contém {string.Join(" e ", missing)} do overlay. Instale uma versão empacotada com PhantomOverlay/linux.";
            return false;
        }

        try
        {
            Directory.CreateDirectory(UserLibraryDirectory);
            Directory.CreateDirectory(UserImplicitLayerDirectory);

            CopyAtomically(BundledVulkanLibrary, InstalledVulkanLibrary);
            CopyAtomically(BundledOpenGlLibrary, InstalledOpenGlLibrary);
            WriteManifestWithInstalledLibraryPath();
            EnsureHudExecutableInstalled();

            message = "Renderizadores Vulkan e OpenGL preparados e comando phantom-hud pronto para este usuário.";
            return true;
        }
        catch (Exception ex)
        {
            message = $"Não foi possível preparar os renderizadores Linux: {ex.Message}";
            return false;
        }
    }

    private static void EnsureHudExecutableInstalled()
    {
        try
        {
            Directory.CreateDirectory(UserBinDirectory);
            if (File.Exists(BundledHudExecutable))
            {
                CopyAtomically(BundledHudExecutable, InstalledHudExecutable);
            }
            else
            {
                var content = GenerateHudScriptContent();
                var temporary = InstalledHudExecutable + ".new-" + Environment.ProcessId;
                try
                {
                    File.WriteAllText(temporary, content, new UTF8Encoding(false));
                    File.Move(temporary, InstalledHudExecutable, overwrite: true);
                }
                finally
                {
                    if (File.Exists(temporary)) File.Delete(temporary);
                }
            }

            if (OperatingSystem.IsLinux() && File.Exists(InstalledHudExecutable))
            {
                File.SetUnixFileMode(InstalledHudExecutable,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }
        }
        catch (Exception ex)
        {
            ErrorLog.Write("LinuxOverlayNativeFiles.EnsureHudExecutable", ex);
        }
    }

    private static string GenerateHudScriptContent()
    {
        var sb = new StringBuilder();
        sb.AppendLine("#!/usr/bin/env bash");
        sb.AppendLine("# Phantom Monitor HUD Wrapper");
        sb.AppendLine("# Uso: phantom-hud <comando_do_jogo> [argumentos...]");
        sb.AppendLine();
        sb.AppendLine("if [ $# -eq 0 ]; then");
        sb.AppendLine("    echo \"Phantom Monitor HUD (phantom-hud)\"");
        sb.AppendLine("    echo \"Uso: phantom-hud <comando_do_jogo> [argumentos...]\"");
        sb.AppendLine("    echo \"\"");
        sb.AppendLine("    echo \"Exemplos:\"");
        sb.AppendLine("    echo \"  Steam:             phantom-hud %command%\"");
        sb.AppendLine("    echo \"  Prism / MultiMC:   defina o wrapper command como: phantom-hud\"");
        sb.AppendLine("    echo \"  Lutris / Heroic:   coloque como prefixo de comando: phantom-hud\"");
        sb.AppendLine("    echo \"  Terminal:          phantom-hud ./meujogo\"");
        sb.AppendLine("    exit 1");
        sb.AppendLine("fi");
        sb.AppendLine();
        sb.AppendLine("OPENGL_LIB=\"${HOME}/.local/lib/libPhantomOpenGLOverlay.so\"");
        sb.AppendLine("export ENABLE_PHANTOM_OVERLAY=1");
        sb.AppendLine("export PHANTOM_FPS_CAPTURE=1");
        sb.AppendLine("unset DISABLE_PHANTOM_OVERLAY");
        sb.AppendLine();
        sb.AppendLine("if [ -f \"${OPENGL_LIB}\" ]; then");
        sb.AppendLine("    export LD_PRELOAD=\"${OPENGL_LIB}${LD_PRELOAD:+:$LD_PRELOAD}\"");
        sb.AppendLine("fi");
        sb.AppendLine();
        sb.AppendLine("exec \"$@\"");
        return sb.ToString();
    }

    private static void CopyAtomically(string source, string destination)
    {
        var temporary = destination + ".new-" + Environment.ProcessId;
        try
        {
            File.Copy(source, temporary, overwrite: true);
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static void WriteManifestWithInstalledLibraryPath()
    {
        var root = JsonNode.Parse(File.ReadAllText(BundledManifest)) as JsonObject
            ?? throw new InvalidDataException("O manifesto Vulkan empacotado é inválido.");
        var layer = root["layer"] as JsonObject
            ?? throw new InvalidDataException("O manifesto Vulkan não contém a camada.");
        layer["library_path"] = Path.GetFullPath(InstalledVulkanLibrary);

        var temporary = InstalledManifest + ".new-" + Environment.ProcessId;
        try
        {
            File.WriteAllText(temporary, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, InstalledManifest, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
