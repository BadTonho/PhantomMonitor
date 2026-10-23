using PhantomMonitor.Models;

namespace PhantomMonitor.Services;

/// <summary>
/// Exportador HTTP do SDK de telemetria (P6.3): publica o último snapshot como
/// JSON em <c>GET /values</c> (mesmo payload do sensors_latest.json) e
/// <c>GET /health</c>. Por padrão desligado e escutando no loopback
/// (127.0.0.1, sem exigir permissão de URL do http.sys); <c>listenAll</c>
/// abre para a LAN (<c>http://+:porta/</c>) e pode requerer liberação de
/// firewall. UDP não foi implementado de propósito: HTTP dá discovery
/// (health), Content-Length e cache headers sem nenhum parser no cliente —
/// justificativa completa em docs/telemetry-sdk.md.
/// </summary>
public sealed class TelemetryExportConfig
{
    public bool Enabled { get; set; }

    /// <summary>Porta TCP do listener (1024–65535; padrão 7317).</summary>
    public int Port { get; set; } = 7317;

    /// <summary>false = escuta só em 127.0.0.1; true = todas as interfaces (LAN, opt-in).</summary>
    public bool ListenAll { get; set; }
}

/// <summary>
/// Verificação de atualizações via GitHub Releases. <c>repository</c> vazio
/// usa o repositório oficial (BadTonho/PhantomMonitor); <c>checkBeta</c>
/// aceita releases marcadas como pré-release; <c>autoCheck</c> liga a
/// checagem automática (no arranque + timer de 24h) da UI.
/// </summary>
public sealed class UpdateConfig
{
    public bool AutoCheck { get; set; } = true;

    /// <summary>Canal beta: aceita releases marcadas como pré-release no GitHub.</summary>
    public bool CheckBeta { get; set; }

    /// <summary>Repositório no formato "owner/repo" (vazio = repositório oficial).</summary>
    public string Repository { get; set; } = "";
}

public sealed class AlertsConfig
{
    public bool Enabled { get; set; } = true;
    public int CooldownSeconds { get; set; } = 60;

    /// <summary>Trava de segurança: sem true, regras com a ação "shutdown" são ignoradas.</summary>
    public bool AllowShutdown { get; set; }

    public List<AlertRule> Rules { get; set; } = [];
}
