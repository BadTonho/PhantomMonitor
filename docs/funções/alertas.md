# Alertas (AlertRuleEvaluator / AlertService)

> **Idioma:** Português (Brasil) · [English](../en/functions/alerts.md)

Regras de alerta sobre sensores avaliadas a cada snapshot (mesma cadência do monitoramento). A comparação, o disparo por condição, o cooldown e a checagem de sensores ausentes ficam no avaliador compartilhado do Core; cada plataforma apresenta os disparos e executa somente as ações que suporta.

## Interface

- **App único:** a página **Alertas** permite ativar a avaliação, ajustar o cooldown global e criar, editar, ativar ou remover regras. O seletor combina séries detectadas com sensores já usados em regras salvas, mesmo que estejam temporariamente ausentes. Limite, comparação, índice da série, cooldown, mensagem, script e argumentos são configuráveis. As alterações só entram em vigor depois de salvar `config.json`.
- O app exibe o toast interno por 6 segundos quando `popup` está ativo e mantém os 50 disparos mais recentes durante a execução do app. Quando a janela está inativa ou minimizada, envia também uma notificação nativa pelo serviço freedesktop `org.freedesktop.Notifications` na sessão D-Bus; o aviso mais recente substitui o anterior. Regras sem `popup` ainda aparecem no histórico.
- Se a sessão D-Bus ou o serviço de notificações não estiver disponível, o erro é registrado e a coleta, o toast interno e o histórico continuam funcionando. Não é necessário instalar `notify-send`.
- Som e desligamento são preservados ao salvar no Linux, mas não são executados no Linux. Scripts configurados são iniciados quando a regra dispara; isso não depende de `popup`.

## Como funciona

- **Disparo por borda:** a ação dispara quando a condição passa de falsa para verdadeira.
- **Cooldown:** enquanto a condição permanece verdadeira, a regra re-dispara a cada `cooldownSeconds` (evita spam de popups). O estado é atrelado à **identidade de vigilância** da regra (sensor+índice, comparador, limite), não ao índice na lista — reordenar regras em runtime não desloca cooldowns, e editar campos de apresentação (mensagem, cooldown) também não reinicia o estado.
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
  real — por isso `"comparison": "Above"` com `"threshold": 0` detecta que já
  ocorreu ao menos um evento WHEA na sessão. Dispara na transição de falso para
  verdadeiro e pode disparar novamente após o cooldown enquanto o contador
  continuar acima de zero; não detecta cada incremento como um evento separado):
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

- **popup**: toast no canto inferior direito do dashboard (`RecordAlert`, port do `AlertPopupWindow` do WPF aposentado), topmost, sem roubar foco, fechando em 6 s ou com um clique; um novo alerta substitui o anterior. No Linux, quando a janela está **fora de foco**, o mesmo disparo vira notificação de desktop (DBus).
- **sound**: bipe de aviso do sistema (`MessageBeep`, ícone de exclamação) — executado pelo app unificado **no Windows** (Core `WindowsAlertActions`). No Linux não há bipe de sistema: o som fica por conta da própria notificação de desktop.
- **script**: executa o arquivo configurado (`.exe`/`.bat`/`.cmd`/`.ps1` via associação padrão, `UseShellExecute`) com os argumentos informados; os placeholders `{value}` e `{sensor}` nos argumentos são substituídos com **cultura invariante** (ponto decimal — o `cmd` trata a vírgula como separador de argumentos). Válido no app unificado no Windows.
- **script no Linux (app unificado)**: arquivos `.sh` são executados por `/bin/sh` e não precisam de permissão de execução; outros arquivos são iniciados diretamente e precisam ter ao menos um bit de execução Unix (`LinuxAlertScriptRunner`, hoje no Core). O caminho pode ser digitado ou escolhido pelo seletor de arquivos. Argumentos são separados por espaços; aspas simples/duplas agrupam um argumento e são removidas, e `\` escapa o caractere seguinte. `{value}` é substituído por valor com uma casa decimal e ponto decimal; `{sensor}` pelo nome da série. Os argumentos são passados sem shell, então pipes, redirecionamentos, expansões e encadeamento de comandos não são interpretados. Aspas não fechadas ou `\` final impedem a execução e são registrados.
- **shutdown**: agenda `shutdown /s /t 60 /c "<mensagem>"` (60 s de carência; cancele com `shutdown /a`). **Só executa se `allowShutdown=true`** — caso contrário registra a mesma mensagem de guarda em `logs/errors.log` (`defina alerts.allowShutdown=true`) e ignora. No Windows é executado pelo app unificado; **no Linux não é executado** (ver Limites).

## Mensagem

- Placeholders disponíveis: `{sensor}`, `{value}` (cultura local, ex.: `90,0`) e `{threshold}`.
- Vazia = padrão `Sensor {sensor}: {value} (limite: {threshold})`.

## Diagnóstico

- Cada disparo é registrado em `logs/errors.log` (tag `[AlertActions]`; no WPF aposentado era `[AlertService]`) com sensor, condição e valor.
- Script inexistente, arquivo não executável, argumentos malformados, falhas ao iniciar e ação `shutdown` bloqueada são registrados sem interromper o monitoramento.
- O Editor de Overlay salva o `config.json` preservando o bloco `alerts`.

## Limites conhecidos

- **Som e desligamento no Linux (app unificado):** não são executados — o som fica
  por conta da notificação de desktop (que carrega o próprio som) e o desligamento
  por alerta é Windows-only; as configurações continuam honradas no config e são
  preservadas. **No Windows** (paridade da F1.5) som, script e desligamento
  executam com paridade integral, incluindo o guard `allowShutdown`.
- A avaliação usa o valor do snapshot no momento da atualização; picos entre amostras não são capturados.
- O desligamento depende do `shutdown.exe` do Windows (políticas restritas podem impedi-lo; a falha fica no log).

## Independência das regras
Cada objeto de regra possui estado de ativação e cooldown próprio. Regras com o
mesmo sensor, comparação e limite podem executar ações diferentes no mesmo ciclo;
uma não suprime a outra. Salvar recria o avaliador com o novo conjunto de regras.
