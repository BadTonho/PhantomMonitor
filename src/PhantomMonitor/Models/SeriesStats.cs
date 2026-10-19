namespace PhantomMonitor.Models;

/// <summary>
/// Estatística de uma série acumulada na sessão de monitoramento (efeito HWiNFO):
/// mínimo, máximo e média das amostras efetivamente publicadas. Zera com
/// "Resetar estatísticas" (bandeja/inventário), que também grava um marcador no CSV.
/// NaN = nenhuma amostra ainda (a média só fica definida com Count ≥ 1).
/// </summary>
public sealed class SeriesStats
{
    public double Min { get; set; } = double.NaN;
    public double Max { get; set; } = double.NaN;
    public double Sum { get; set; }
    public int Count { get; set; }

    public double Average => Count == 0 ? double.NaN : Sum / Count;

    /// <summary>Amostra não-finita (NaN/Inf de leitura balançada do driver) nunca entra.</summary>
    public void Add(double value)
    {
        if (!double.IsFinite(value)) return;

        if (!double.IsFinite(Min) || value < Min) Min = value;
        if (!double.IsFinite(Max) || value > Max) Max = value;
        Sum += value;
        Count++;
    }
}
