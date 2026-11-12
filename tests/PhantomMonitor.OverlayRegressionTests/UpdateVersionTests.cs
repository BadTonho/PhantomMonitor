using PhantomMonitor.Services.Update;

public sealed class UpdateVersionTests
{
    [Theory]
    [InlineData("0.1.0", false)]
    [InlineData("1.2.3", false)]
    [InlineData("1.0.0-beta1", false)]
    [InlineData("0.0.0-dev", true)]
    [InlineData("0.0.0", true)]
    [InlineData("", true)]
    [InlineData("abc", true)]
    [InlineData(null, true)]
    public void Build_de_release_nao_e_tratado_como_desenvolvimento(string? version, bool esperadoDev)
        => Assert.Equal(esperadoDev, UpdateVersion.IsDevelopmentVersion(version));

    [Fact]
    public void Release_posterior_e_considerada_mais_nova_e_nunca_rebaixa()
    {
        Assert.True(UpdateVersion.TryParse("v0.1.1", out var nova));
        Assert.True(UpdateVersion.TryParse("0.1.0", out var atual));
        Assert.True(nova.CompareTo(atual) > 0);
        Assert.True(atual.CompareTo(nova) < 0);
        Assert.True(UpdateVersion.TryParse("0.1.0-rc1", out var rc));
        Assert.True(rc.CompareTo(atual) < 0);
    }
}
