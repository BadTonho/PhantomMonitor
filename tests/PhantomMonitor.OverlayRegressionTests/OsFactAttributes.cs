/// <summary>[Fact] que só executa no Windows; nos demais SO o teste aparece como ignorado (SKIP).</summary>
public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute() { if (!OperatingSystem.IsWindows()) Skip = "Requer Windows."; }
}

/// <summary>[Fact] que só executa no Linux; nos demais SO o teste aparece como ignorado (SKIP).</summary>
public sealed class LinuxFactAttribute : FactAttribute
{
    public LinuxFactAttribute() { if (!OperatingSystem.IsLinux()) Skip = "Requer Linux."; }
}

