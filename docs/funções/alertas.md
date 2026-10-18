# Alertas (AlertService)

Regras de alerta sobre sensores com ações automáticas — **popup, som, script e desligamento** — avaliadas a cada snapshot de sensores (mesma cadência do monitoramento), sem dependências externas.

## Como funciona

- **Disparo por borda:** a ação dispara quando a condição passa de falsa para verdadeira.
- **Cooldown:** enquanto a condição permanece verdadeira, a regra re-dispara a cada `cooldownSeconds` (evita spam de popups). O estado é atrelado ao **conteúdo** da regra (sensor/limite/mensagem), não ao índice na lista — reordenar regras no runtime não desloca cooldowns.
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
