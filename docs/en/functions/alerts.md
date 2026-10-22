> **Language:** English · [Português (Brasil)](../../funções/alertas.md)

# Alerts (`AlertRuleEvaluator` / `AlertService`)

Sensor alert rules are evaluated for each snapshot. The shared Core evaluator handles comparisons, edge-triggering, cooldowns, and missing sensors; each frontend presents alerts and runs only the actions it supports.

## Frontends

- **Avalonia/Linux:** The **Alerts** page can enable evaluation, set a global cooldown, and create, edit, enable, or remove rules. The sensor picker combines detected series with saved rule targets, including temporarily missing sensors. Rules include a series index, comparison, threshold, cooldown, message, script, and arguments. Changes take effect only after `config.json` is saved.
- When `popup` is enabled, Avalonia shows an in-app toast for six seconds and keeps the 50 most recent alerts for the lifetime of the app. If the window is inactive or minimized, it also sends a desktop notification through `org.freedesktop.Notifications` on the session D-Bus. A new desktop alert replaces the previous one. Rules without `popup` still appear in history.
- If D-Bus or the desktop notification service is unavailable, the error is logged; collection, in-app toasts, and history continue. `notify-send` is not required.
- Linux preserves sound and shutdown settings but does not run those actions. Configured scripts run when a rule fires, independently of `popup`. WPF retains its current actions.

## Evaluation semantics

- **Edge trigger:** an action fires when a condition changes from false to true.
- **Cooldown:** while the condition remains true, it can fire again after `cooldownSeconds`. State is tied to rule content (sensor/index, comparison, threshold, message, and cooldown), not its list position.
- **Rearm:** when the condition becomes false, the rule is rearmed and fires on the next transition to true.
- **Missing sensor:** a sensor is missing only if it is absent from the snapshot. A present value of `0` is valid; for example, `Below` can alert on a fan at 0 RPM.

## `config.json` schema

```json
"alerts": {
  "enabled": true,
  "cooldownSeconds": 60,
  "allowShutdown": false,
  "rules": [{
    "enabled": true,
    "sensor": "CpuTemperature",
    "sensorIndex": 0,
    "comparison": "Above",
    "threshold": 90,
    "cooldownSeconds": 60,
    "popup": true,
    "sound": true,
    "script": "C:\\scripts\\alert.bat",
    "scriptArguments": "{value}",
    "shutdown": false,
    "message": "CPU is {value} °C (limit {threshold} °C)"
  }]
}
```

The global cooldown is 1–3600 seconds; per-rule `0` uses the global value. `sensorIndex` 0 addresses the canonical series; 1 or higher addresses an instance, such as dedicated GPU temperature at index 1. Custom sensors use `sensor: "Custom"` and the formula's position as `sensorIndex`. See [data models](data-models.md) for sensor types and [hardware monitoring](hardware-monitor.md) for instance numbering.

## Actions

- **`popup`:** WPF displays a topmost, non-activating alert window for six seconds or until clicked. Avalonia uses its in-app toast and, while the window is inactive or minimized, a native desktop notification.
- **`sound`:** Windows system warning beep (`MessageBeep`). Avalonia preserves this setting but does not play it.
- **`script`:** WPF starts the configured Windows file type with its default association. `{value}` and `{sensor}` are substituted in arguments using invariant decimal formatting. On Linux, `.sh` files run through `/bin/sh` and need not be executable; other files are started directly and need a Unix execute bit. The Avalonia picker accepts a path, and arguments are split into tokens with single/double quoting and backslash escaping. `{value}` uses a decimal point and one fractional digit; `{sensor}` is the series key. Arguments are passed without a shell, so pipes, redirects, expansions, and command chaining are not interpreted. Malformed quoting and launch errors are logged.
- **`shutdown`:** WPF schedules `shutdown /s /t 60`; it runs only when `allowShutdown` is true. Avalonia does not execute shutdown actions.

Message placeholders are `{sensor}`, `{value}` (locale-formatted in WPF), and `{threshold}`. An empty message uses `Sensor {sensor}: {value} (limit: {threshold})`.

For example, a Windows WHEA rule using `sensor: "WheaErrorCount"`, comparison `Above`, and threshold `0` fires on each newly observed hardware-error count (subject to cooldown); zero itself is still a real reading. A custom formula is addressed by `sensor: "Custom"` and its one-based `sensorIndex`. On instance rules, the message's `{sensor}` contains the suffixed key, such as `GpuTemperature#1`.

WPF's popup is topmost and does not take focus; a new alert replaces the current one. Its sound action is the system warning beep. Windows scripts can use the default file association for `.exe`, `.bat`, `.cmd`, or `.ps1`. Shutdown schedules `shutdown /s /t 60 /c "<message>"`; cancel it with `shutdown /a`. If `allowShutdown` is false, the action is skipped and the reason is recorded in `logs/errors.log`.

## Diagnostics and limits

Alert events and script launch failures are written to `logs/errors.log`; failures do not stop monitoring. The Avalonia editor preserves existing sound and shutdown fields when saving. Evaluation uses the value in each snapshot, so peaks between samples are not observed. WHEA is a Windows-only counter, and shutdown requires Windows `shutdown.exe`.

In Avalonia, the toast lasts six seconds; a new native D-Bus notification replaces the previous desktop notification, while both alerts remain in the 50-entry in-memory history. Script parsing rejects an unmatched quote or a trailing escape character. Script paths that do not exist, non-executable non-`.sh` files, and process-start failures are logged without suppressing the history entry or ending collection.
