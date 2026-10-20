# Alertas (AlertRuleEvaluator / AlertService)

Regras de alerta sobre sensores avaliadas a cada snapshot (mesma cadência do monitoramento). A comparação, o disparo por condição, o cooldown e a checagem de sensores ausentes ficam no avaliador compartilhado do Core; cada frontend apresenta os disparos e executa somente suas ações suportadas.

## Frontends

- **Avalonia/Linux:** a página **Alertas** permite ativar a avaliação, ajustar o cooldown global e criar, editar, ativar ou remover regras. O seletor combina séries detectadas com sensores já usados em regras salvas, mesmo que estejam temporariamente ausentes. Limite, comparação, índice da série, cooldown e mensagem são configuráveis. As alterações só entram em vigor depois de salvar `config.json`.
- O Avalonia exibe uma notificação interna por 6 segundos quando `popup` está ativo e mantém os 50 disparos mais recentes durante a execução do app. Um novo aviso substitui o visível; todos continuam no histórico. Regras sem `popup` ainda aparecem no histórico.
- Som, scripts e desligamento são preservados ao salvar no Linux, mas não são executados pelo Avalonia. No WPF, as ações documentadas abaixo continuam disponíveis.

## Como funciona

- **Disparo por borda:** a ação dispara quando a condição passa de falsa para verdadeira.
- **Cooldown:** enquanto a condição permanece verdadeira, a regra re-dispara a cada `cooldownSeconds` (evita spam de popups). O estado é atrelado ao **conteúdo** da regra (sensor+índice, comparador, limite, mensagem e cooldown), não ao índice na lista — reordenar regras no runtime não desloca cooldowns.
- **Rearme:** quando a condição volta a ser falsa, a regra é rearmada e dispara imediatamente na próxima vez que for verdadeira.
- **Sensor ausente:** é ausente apenas quando o sensor **não aparece no snapshot** (leitura nunca ocorreu) — nesse caso a regra não dispara nem mantém o estado ativo. Um valor **0 presente** é leitura real e válido: regras `Below` disparam normalmente (ex.: ventoinha parada em 0 RPM).

## Schema (`config.json`)

```json
"alerts": {
  "enabled": true,           // liga/desliga todas as regras
  "cooldownSeconds": 60,     // cooldown padrão em segundos (1–3600; a regra pode sobrescrever)
  "allowShutdown": false,    // trava de segurança da ação "shutdown"
  "rules": [
    {
      "enabled": true,
      "sensor": "CpuTemperature",   // qualquer SensorType (ver modelos-dados.md)
      "sensorIndex": 0,             // 0 = série canônica; ≥ 1 = série por instância
                                    // (ex.: GpuTemperature índice 1 = GPU dedicada)
      "comparison": "Above",        // Above | Below
      "threshold": 90,
      "cooldownSeconds": 60,        // 0 = usa o cooldown global
      "popup": true,
      "sound": true,
      "script": "C:\\scripts\\alerta.bat",
      "scriptArguments": "{value}",
      "shutdown": false,
      "message": "CPU em {value} °C (limite {threshold} °C)"
    }
  ]
}
```

O `config.example.json` traz uma regra de exemplo **desabilitada** com todos os campos.

## Exemplos úteis (séries novas)

- **Erro de hardware WHEA** (`WheaErrorCount`, Windows; contador em `0` é leitura
  real —por isso `"comparison": "Above"` com `"threshold": 0` dispara em
  **qualquer erro novo** da sessão e respeita o cooldown no log cheio de repetições):
  ```json
  { "enabled": true, "sensor": "WheaErrorCount", "comparison": "Above",
    "threshold": 0, "popup": true, "sound": true,
    "message": "Erro de hardware WHEA registrado ({value} na sessão)" }
  ```
- **Série customizada** (`Custom#i`): `"sensor": "Custom"` + `sensorIndex` da
  fórmula (ordem no `customSensors` do config).

## Alertas por instância

`sensorIndex` (padrão 0) mira séries **por instância**: `"sensor": "GpuTemperature",
"sensorIndex": 1` alerta sobre a GPU dedicada; `"sensor": "CpuTemperature",
"sensorIndex": 2` sobre o núcleo 2. O índice segue a numeração documentada em
[hardware-monitor.md](hardware-monitor.md) (dedicada = GPU 1; núcleos/heranças
seguem o número do sensor no hardware). A mensagem usa o sensor com sufixo
(`GpuTemperature#1`) e o placeholder `{sensor}` nos scripts também o inclui.

## Ações

- **popup**: toast no canto inferior direito (`AlertPopupWindow`) — topmost, não rouba foco, fecha em 6 s ou com um clique; um novo alerta substitui o anterior.
- **sound**: bipe de aviso do sistema (`MessageBeep`, ícone de exclamação).
- **script**: executa o arquivo configurado (`.exe`/`.bat`/`.cmd`/`.ps1` via associação padrão) com os argumentos informados; os placeholders `{value}` e `{sensor}` nos argumentos são substituídos com **cultura invariante** (ponto decimal — o `cmd` trata a vírgula como separador de argumentos).
- **shutdown**: agenda `shutdown /s /t 60 /c "<mensagem>"` (60 s de carência; cancele com `shutdown /a`). **Só executa se `allowShutdown=true`** — caso contrário registra o motivo em `logs/errors.log` e ignora.

## Mensagem

- Placeholders disponíveis: `{sensor}`, `{value}` (cultura local, ex.: `90,0`) e `{threshold}`.
- Vazia = padrão `Sensor {sensor}: {value} (limite: {threshold})`.

## Diagnóstico

- Cada disparo é registrado em `logs/errors.log` (`[AlertService]`) com sensor, condição e valor.
- Script inexistente, ação `shutdown` bloqueada e falhas de execução são registrados sem interromper o monitoramento.
- O Editor de Overlay salva o `config.json` preservando o bloco `alerts`.

## Limites conhecidos

- Sem UI de edição: as regras são configuradas no `config.json`.
- A avaliação usa o valor do snapshot no momento da atualização; picos entre amostras não são capturados.
- O desligamento depende do `shutdown.exe` do Windows (políticas restritas podem impedi-lo; a falha fica no log).
