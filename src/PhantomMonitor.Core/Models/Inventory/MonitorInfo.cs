namespace PhantomMonitor.Models;

using System.Text.Json.Serialization;

/// <summary>
/// Identidade de um monitor conectado (paridade EDID de suítes de referência):
/// decodificada dos recursos WMI <c>WmiMonitorID</c> (fab/modelo/serial/ano, e
/// <c>WmiMonitorBasicDisplayParams</c> para a diagonal física). Vai para o
/// snapshot JSON (<c>monitors</c>) e para a aba "Monitores" do inventário; não
/// entra no CSV nem na tabela da SDK (são séries numéricas apenas). Desktop sem
/// monitor reportado pelo WMI publica lista vazia.
/// </summary>
public sealed class MonitorInfo
{
    /// <summary>Código PnP do fabricante decodificado do EDID ("DEL", "SAM", "LGD"...).</summary>
    public string Manufacturer { get; set; } = "";

    /// <summary>Nome amigável do painel do EDID ("U2723QE"); vazio quando o EDID não expõe.</summary>
    public string Model { get; set; } = "";

    /// <summary>ProductCodeID reportado pelo WMI (texto do EDID ou número decodificado).</summary>
    public string ProductCode { get; set; } = "";

    /// <summary>Nº de série do painel (SerialNumberID decodificado).</summary>
    public string Serial { get; set; } = "";

    /// <summary>Diagonal física em polegadas (√(largura²+altura²)/2.54; 0 = não reportado).</summary>
    public double DiagonalInches { get; set; }

    /// <summary>Ano de fabricação (EDID; 0 = não reportado).</summary>
    public int YearOfManufacture { get; set; }

    /// <summary>Semana de fabricação no ano (EDID; 0 = não reportado).</summary>
    public int WeekOfManufacture { get; set; }

    /// <summary>Caminho da instância WMI (identifica o device; útil com múltiplos monitores).</summary>
    [JsonIgnore]
    public string InstanceName { get; set; } = "";

    /// <summary>True para o monitor primário (bit da propriedade VideoInputType de
    /// WmiMonitorBasicDisplayParams; false quando não determinável).</summary>
    public bool Primary { get; set; }

    /// <summary>Diagonal formatada para o inventário ("27.5&quot;"; "--" quando não reportada).</summary>
    [JsonIgnore]
    public string DiagonalText => DiagonalInches > 0 ? $"{DiagonalInches:F1}\"" : "--";

    /// <summary>Fabricação formatada para o inventário ("2023 s24"; "--" quando não reportada).</summary>
    [JsonIgnore]
    public string MadeText => YearOfManufacture > 0
        ? $"{YearOfManufacture}{(WeekOfManufacture > 0 ? $" s{WeekOfManufacture}" : "")}"
        : "--";
}
