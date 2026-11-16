using PhantomMonitor.App.Services.Overlay;

public sealed class LinuxLaunchEnvironmentTests
{
    [Fact]
    public void Servicos_inexistentes_do_Plasma_6_nao_sao_tratados_como_falha()
    {
        const string error = "Plasma: não confirmou LD_PRELOAD: Error org.freedesktop.DBus.Error.ServiceUnknown: The name is not activatable; "
            + "KLauncher: não confirmou LD_PRELOAD: Error org.freedesktop.DBus.Error.ServiceUnknown: The name is not activatable";
        Assert.True(LinuxDesktopLaunchEnvironment.AllServicesAbsent(error));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Plasma: não confirmou LD_PRELOAD: tempo limite excedido")]
    [InlineData("Plasma: não confirmou LD_PRELOAD: ServiceUnknown; KLauncher: não confirmou LD_PRELOAD: acesso negado")]
    public void Falhas_reais_continuam_sendo_reportadas(string error)
        => Assert.False(LinuxDesktopLaunchEnvironment.AllServicesAbsent(error));
}
