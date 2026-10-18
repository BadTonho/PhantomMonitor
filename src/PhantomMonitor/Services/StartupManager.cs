using System.IO;
using Microsoft.Win32;

namespace PhantomMonitor.Services;

/// <summary>
/// Liga/desliga a inicialização automática com o Windows via chave Run do usuário
/// (<c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>) — não requer administrador
/// e usa o caminho do executável atual.
/// </summary>
public static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "PhantomMonitor";

    public static bool IsEnabled()
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
            ErrorLog.Write("StartupManager.IsEnabled", ex);
            return false;
        }
    }

    public static bool SetEnabled(bool enabled)
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
            ErrorLog.Write("StartupManager.SetEnabled", ex);
            return false;
        }
    }
}
