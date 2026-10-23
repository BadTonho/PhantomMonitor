using System.IO;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace PhantomMonitor.Services;

/// <summary>
/// Liga/desliga a inicialização automática com o Windows via chave Run do usuário
/// (<c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>) — não requer
/// administrador e usa o caminho do executável atual. Porta do StartupManager do WPF.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsStartupManager : IStartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "PhantomMonitor";

    public static WindowsStartupManager Shared { get; } = new();

    public bool IsEnabled()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return false;

            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            var value = key?.GetValue(ValueName) as string;
            if (string.IsNullOrEmpty(value)) return false;

            // Compara com o executável atual (aceitando com/sem aspas e diferenças de caixa).
            var registered = value.Trim().Trim('"');
            return string.Equals(registered, exe.Trim('"'), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            ErrorLog.Write("WindowsStartupManager.IsEnabled", ex);
            return false;
        }
    }

    public bool SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (key == null) return false;

            if (enabled)
            {
                var exe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exe) || !File.Exists(exe)) return false;
                key.SetValue(ValueName, $"\"{exe}\"");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
            return true;
        }
        catch (Exception ex)
        {
            ErrorLog.Write("WindowsStartupManager.SetEnabled", ex);
            return false;
        }
    }
}
