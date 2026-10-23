using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PhantomMonitor.Services;

/// <summary>
/// Interop do user32 compartilhado entre overlay, editor de overlay e FpsMonitor —
/// antes os mesmos DllImport eram declarados três vezes, uma vez por classe.
/// Assinaturas preservadas idênticas às declaradas nos chamadores.
/// </summary>
[SupportedOSPlatform("windows")]
public static class Win32Interop
{
    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
}
