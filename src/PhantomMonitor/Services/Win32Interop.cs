using System.Runtime.InteropServices;

namespace PhantomMonitor.Services;

/// <summary>
/// Interop do user32 compartilhado entre overlay, editor de overlay e FpsMonitor —
/// antes os mesmos DllImport eram declarados três vezes, uma vez por classe.
/// Assinaturas preservadas idênticas às declaradas nos chamadores.
/// </summary>
internal static class Win32Interop
{
    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
}
