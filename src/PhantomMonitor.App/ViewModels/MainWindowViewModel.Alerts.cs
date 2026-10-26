using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Collections.ObjectModel;
using System.Threading;
using Avalonia.Threading;
using PhantomMonitor.App.Services;
using PhantomMonitor.Models;
using PhantomMonitor.Services;
using PhantomMonitor.Services.Linux;
using PhantomMonitor.Services.Telemetry;

namespace PhantomMonitor.App.ViewModels;

// Alertas: regras, opções vindas do inventário, histórico, toasts e estado da janela.
public sealed partial class MainWindowViewModel
{
    public void AddAlertRule()
    {
        if (AlertSensorOptions.Count == 0)
        {
            AlertsMessage = "Aguarde a primeira leitura para detectar séries de sensores.";
            return;
        }

        AlertRules.Add(new AlertRuleEditorViewModel(new AlertRule
        {
            Sensor = SensorKey.TryParse(AlertSensorOptions[0], out var key) ? key.Type : SensorType.CpuTemperature,
            SensorIndex = key.Index,
            Threshold = 80,
            Popup = true
        }, AlertSensorOptions));
        AlertsMessage = "Regra adicionada. Salve para aplicá-la.";
    }

    public void RemoveAlertRule(AlertRuleEditorViewModel rule)
    {
        AlertRules.Remove(rule);
        AlertsMessage = "Regra removida. Salve para aplicar a alteração.";
    }

    public void SaveAlerts()
    {
        if (!int.TryParse(AlertsCooldownText, NumberStyles.Integer, CultureInfo.CurrentCulture, out var cooldown)
            || cooldown is < 1 or > 3600)
        {
            AlertsMessage = "O cooldown global deve ficar entre 1 e 3.600 segundos.";
            return;
        }

        var candidateRules = new List<AlertRule>(AlertRules.Count);
        foreach (var editor in AlertRules)
        {
            if (!SensorKey.TryParse(editor.SensorKey, out var sensorKey))
            {
                AlertsMessage = $"Sensor inválido na regra: {editor.SensorKey}";
                return;
            }
            if (!double.TryParse(editor.ThresholdText, NumberStyles.Float, CultureInfo.CurrentCulture, out var threshold)
                || !double.IsFinite(threshold))
            {
                AlertsMessage = $"Informe um limite numérico válido para {editor.SensorKey}.";
                return;
            }
            if (!int.TryParse(editor.CooldownText, NumberStyles.Integer, CultureInfo.CurrentCulture, out var ruleCooldown)
                || ruleCooldown is < 0 or > 3600)
            {
                AlertsMessage = $"O cooldown da regra {editor.SensorKey} deve ficar entre 0 e 3.600 segundos.";
                return;
            }
            if (!Enum.TryParse<AlertComparison>(editor.Comparison, out _))
            {
                AlertsMessage = $"Comparação inválida na regra {editor.SensorKey}.";
                return;
            }
            if (sensorKey.Index < 0)
            {
                AlertsMessage = $"Índice de sensor inválido: {editor.SensorKey}";
                return;
            }

            candidateRules.Add(editor.CreateRule(threshold, ruleCooldown));
        }

        var previous = _config.Alerts;
        var candidate = new AlertsConfig
        {
            Enabled = AlertsEnabled,
            CooldownSeconds = cooldown,
            AllowShutdown = previous.AllowShutdown,
            Rules = candidateRules
        };
        _config.Alerts = candidate;
        if (!SaveConfiguration())
        {
            _config.Alerts = previous;
            AlertsMessage = "Não foi possível salvar o config.json. As regras em execução não foram alteradas.";
            return;
        }

        _alertEvaluator = new AlertRuleEvaluator(candidate);
        _alertsEnabled = candidate.Enabled;
        _alertsCooldownText = candidate.CooldownSeconds.ToString(CultureInfo.CurrentCulture);
        AlertsMessage = "Alertas salvos e aplicados. Serão avaliados na próxima leitura.";
        Raise(nameof(AlertsEnabled));
        Raise(nameof(AlertsCooldownText));
    }

    private void RefreshAlertEditor()
    {
        var keys = new HashSet<string>(_config.Alerts.Rules.Select(rule => new SensorKey(rule.Sensor, rule.SensorIndex).ToString()), StringComparer.Ordinal);
        foreach (var group in InventoryGroups)
            foreach (var row in group.Rows.Where(row => row.IsAvailable))
                keys.Add(row.Key);

        // Reatribuir ItemsSource a cada snapshot (até 10x/s) re-renderizava cada
        // ComboBox de regra; só troca quando o conjunto de chaves mudar — mesmo
        // guard usado por RefreshSensorTuningSeriesOptions.
        var options = keys.OrderBy(key => key, StringComparer.Ordinal).ToArray();
        if (AlertSensorOptions is null || !AlertSensorOptions.SequenceEqual(options, StringComparer.Ordinal))
        {
            AlertSensorOptions = options;
            foreach (var rule in AlertRules)
                rule.SensorOptions = options;
        }

        if (!_alertEditorInitialized)
        {
            foreach (var rule in _config.Alerts.Rules)
                AlertRules.Add(new AlertRuleEditorViewModel(rule, AlertSensorOptions));
            _alertEditorInitialized = true;
        }
    }

    /// <summary>
    /// Orquestração das ações por alerta (F1.5), na ordem do AlertService do WPF:
    /// som (beep no Windows — a notificação de desktop do Linux carrega o próprio som),
    /// script da regra, e desligamento com guard <c>allowShutdown</c> (mensagem idêntica
    /// à do WPF quando bloqueado). Popup/toast + notificação fora de foco ficam no
    /// RecordAlert, imediatamente depois.
    /// </summary>
    private void ExecuteAlertActions(AlertTrigger trigger)
    {
        try
        {
            if (trigger.Rule.Sound) _alertActions.Sound();
            _alertActions.RunScript(trigger);
            if (trigger.Rule.Shutdown)
            {
                if (_config.Alerts.AllowShutdown)
                    _alertActions.Shutdown(trigger.Message);
                else
                    ErrorLog.Write("AlertActions",
                        "Ação 'shutdown' ignorada: defina alerts.allowShutdown=true no config para permiti-la.");
            }
        }
        catch (Exception ex)
        {
            ErrorLog.Write("AlertActions.Trigger", ex);
        }
    }

    private void RecordAlert(AlertTrigger trigger, DateTime timestamp)
    {        AlertHistory.Insert(0, new AlertHistoryEntryViewModel(
            timestamp.ToLocalTime().ToString("HH:mm:ss"),
            trigger.SensorName,
            trigger.Message,
            trigger.Value.ToString("F1", CultureInfo.CurrentCulture)));
        while (AlertHistory.Count > 50) AlertHistory.RemoveAt(AlertHistory.Count - 1);
        Raise(nameof(HasAlertHistory));

        if (!trigger.Rule.Popup) return;
        AlertToastMessage = trigger.Message;
        AlertToastVisible = true;
        _alertToastTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        _alertToastTimer.Stop();
        _alertToastTimer.Tick -= OnAlertToastTimerTick;
        _alertToastTimer.Tick += OnAlertToastTimerTick;
        _alertToastTimer.Start();

        if (!_windowActive || _windowMinimized)
            _ = SendDesktopNotificationAsync(trigger.Message);
    }

    public void SetWindowPresentationState(bool isActive, bool isMinimized)
    {
        _windowActive = isActive;
        _windowMinimized = isMinimized;
    }

    private async Task SendDesktopNotificationAsync(string message)
    {
        if (_desktopNotificationService is null || _disposed) return;
        try
        {
            await _desktopNotificationService.NotifyAsync(message);
        }
        catch (Exception ex)
        {
            ErrorLog.Write("LinuxDesktopNotification", ex);
        }
    }

    private void OnAlertToastTimerTick(object? sender, EventArgs e)
    {
        _alertToastTimer?.Stop();
        AlertToastVisible = false;
    }

}
