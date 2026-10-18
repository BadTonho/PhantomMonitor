using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using PhantomMonitor.Models;

namespace PhantomMonitor.Services;

/// <summary>
/// Avalia as regras de alerta sobre cada snapshot de sensores e executa as ações
/// configuradas: popup (delegado para a UI), som (MessageBeep), script externo e
/// desligamento do sistema (com trava de segurança <c>allowShutdown</c>).
/// Não depende de WPF: o popup é injetado via <see cref="ShowPopup"/>, o que
/// mantém o serviço testável isoladamente.
/// </summary>
public sealed class AlertService
{
    private readonly AlertsConfig _config;
    private readonly Dictionary<int, DateTime> _lastTriggered = new();
    private readonly HashSet<int> _active = new();

    /// <summary>Exibição do popup (título, mensagem). Sem delegado, o popup é apenas ignorado.</summary>
    public Action<string, string>? ShowPopup { get; set; }

    /// <summary>Notificação de disparo (diagnóstico/testes) com a mensagem já formatada.</summary>
    public event Action<AlertRule, string>? Triggered;

    public AlertService(AlertsConfig config)
    {
        _config = config ?? new AlertsConfig();
    }

    /// <summary>Avalia todas as regras habilitadas contra o snapshot atual.</summary>
    public void Evaluate(SensorData data)
    {
        if (!_config.Enabled || _config.Rules.Count == 0) return;

        for (int i = 0; i < _config.Rules.Count; i++)
        {
            var rule = _config.Rules[i];
            if (rule is not { Enabled: true }) continue;

            // Sensor ausente = chave não presente no snapshot (a leitura nunca ocorreu).
            // Um 0 PRESENTE é leitura real: regras "Below" devem disparar com ele —
            // ex.: ventoinha parada (0 RPM) silenciava o alerta de RPM baixo, e uso
            // de disco em repouso (0%) nunca disparava "disco ocioso".
            if (!data.Values.ContainsKey(rule.Sensor))
            {
                _active.Remove(i);
                continue;
            }

            double value = data.GetValue(rule.Sensor);

            bool condition = rule.Comparison == AlertComparison.Above
                ? value > rule.Threshold
                : value < rule.Threshold;

            if (!condition)
            {
                _active.Remove(ActiveKey(rule));
                continue;
            }

            int key = ActiveKey(rule);
            bool cooldownElapsed = !_lastTriggered.TryGetValue(key, out var last)
                || (DateTime.UtcNow - last).TotalSeconds >= EffectiveCooldown(rule);

            if (!_active.Contains(key) || cooldownElapsed)
            {
                _lastTriggered[key] = DateTime.UtcNow;
                _active.Add(key);
                Trigger(rule, value);
            }
        }
    }

    /// <summary>
    /// Chave de estado por CONTEÚDO da regra (não por índice): reordenar regras no
    /// runtime não desloca cooldowns/estado ativo para outra regra.
    /// </summary>
    private static int ActiveKey(AlertRule rule) =>
        HashCode.Combine(rule.Sensor, rule.Comparison, rule.Threshold, rule.Message, rule.CooldownSeconds);

    private int EffectiveCooldown(AlertRule rule) =>
        rule.CooldownSeconds > 0 ? rule.CooldownSeconds : Math.Max(1, _config.CooldownSeconds);

    private void Trigger(AlertRule rule, double value)
    {
        try
        {
            string message = BuildMessage(rule, value);

            if (rule.Popup) ShowPopup?.Invoke("Phantom Monitor — Alerta", message);
            if (rule.Sound) MessageBeep(0x00000030); // MB_ICONWARNING
            if (!string.IsNullOrWhiteSpace(rule.Script)) RunScript(rule, value);

            if (rule.Shutdown)
            {
                if (_config.AllowShutdown)
                    StartShutdown(message);
                else
                    ErrorLog.Write("AlertService",
                        "Ação 'shutdown' ignorada: defina alerts.allowShutdown=true no config para permiti-la.");
            }

            ErrorLog.Write("AlertService", $"Alerta disparado: {rule.Sensor} {rule.Comparison} {rule.Threshold} (valor {value:F1})");
            Triggered?.Invoke(rule, message);
        }
        catch (Exception ex)
        {
            ErrorLog.Write("AlertService.Trigger", ex);
        }
    }

    private static string BuildMessage(AlertRule rule, double value)
    {
        string template = string.IsNullOrWhiteSpace(rule.Message)
            ? "Sensor {sensor}: {value} (limite: {threshold})"
            : rule.Message;

        return template
            .Replace("{sensor}", rule.Sensor.ToString())
            .Replace("{value}", value.ToString("F1"))
            .Replace("{threshold}", rule.Threshold.ToString("F1"));
    }

    private static void RunScript(AlertRule rule, double value)
    {
        if (!File.Exists(rule.Script))
        {
            ErrorLog.Write("AlertService", $"Script do alerta não encontrado: {rule.Script}");
            return;
        }

        var args = (rule.ScriptArguments ?? "")
            // Cultura invariante: o cmd trata vírgula como separador de argumentos.
            .Replace("{value}", value.ToString("F1", CultureInfo.InvariantCulture))
            .Replace("{sensor}", rule.Sensor.ToString());

        Process.Start(new ProcessStartInfo
        {
            FileName = rule.Script,
            Arguments = args,
            UseShellExecute = true // permite .bat/.cmd/.ps1 com associação padrão
        })?.Dispose();
    }

    private static void StartShutdown(string message)
    {
        // Remove aspas (quebrariam o argumento /c) e limita ao tamanho aceito pelo shutdown.exe.
        string comment = message.Replace("\"", "'").Replace("\r", " ").Replace("\n", " ");
        if (comment.Length > 400) comment = comment[..400];

        Process.Start(new ProcessStartInfo
        {
            FileName = "shutdown",
            Arguments = $"/s /t 60 /c \"{comment}\"",
            UseShellExecute = false,
            CreateNoWindow = true
        })?.Dispose();

        ErrorLog.Write("AlertService", "Desligamento agendado para daqui a 60 s (cancele com 'shutdown /a').");
    }

    [DllImport("user32.dll")]
    private static extern bool MessageBeep(uint uType);
}
