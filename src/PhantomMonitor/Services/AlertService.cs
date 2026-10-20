using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using PhantomMonitor.Models;

namespace PhantomMonitor.Services;

/// <summary>Executa as ações Windows para os disparos calculados pelo avaliador compartilhado.</summary>
public sealed class AlertService
{
    private readonly AlertsConfig _config;
    private readonly AlertRuleEvaluator _evaluator;

    public Action<string, string>? ShowPopup { get; set; }
    public event Action<AlertRule, string>? Triggered;

    public AlertService(AlertsConfig config)
    {
        _config = config ?? new AlertsConfig();
        _evaluator = new AlertRuleEvaluator(_config);
    }

    public void Evaluate(SensorData data)
    {
        foreach (var trigger in _evaluator.Evaluate(data))
            ExecuteActions(trigger);
    }

    private void ExecuteActions(AlertTrigger trigger)
    {
        var rule = trigger.Rule;
        var message = trigger.Message;
        try
        {
            if (rule.Popup) ShowPopup?.Invoke("Phantom Monitor — Alerta", message);
            if (rule.Sound) MessageBeep(0x00000030);
            if (!string.IsNullOrWhiteSpace(rule.Script)) RunScript(rule, trigger.Value, trigger.SensorName);

            if (rule.Shutdown)
            {
                if (_config.AllowShutdown)
                    StartShutdown(message);
                else
                    ErrorLog.Write("AlertService",
                        "Ação 'shutdown' ignorada: defina alerts.allowShutdown=true no config para permiti-la.");
            }

            ErrorLog.Write("AlertService", $"Alerta disparado: {trigger.SensorName} {rule.Comparison} {rule.Threshold} (valor {trigger.Value:F1})");
            Triggered?.Invoke(rule, message);
        }
        catch (Exception ex)
        {
            ErrorLog.Write("AlertService.Trigger", ex);
        }
    }

    private static void RunScript(AlertRule rule, double value, string sensorName)
    {
        if (!File.Exists(rule.Script))
        {
            ErrorLog.Write("AlertService", $"Script do alerta não encontrado: {rule.Script}");
            return;
        }

        var args = (rule.ScriptArguments ?? "")
            .Replace("{value}", value.ToString("F1", CultureInfo.InvariantCulture))
            .Replace("{sensor}", sensorName);

        Process.Start(new ProcessStartInfo
        {
            FileName = rule.Script,
            Arguments = args,
            UseShellExecute = true
        })?.Dispose();
    }

    private static void StartShutdown(string message)
    {
        string comment = message.Replace('"', '\'').Replace('\r', ' ').Replace('\n', ' ');
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
