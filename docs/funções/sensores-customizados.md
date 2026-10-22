# Sensores customizados (fórmulas do usuário)

> **Idioma:** Português (Brasil) · [English](../en/functions/custom-sensors.md)

> Paridade com "custom user sensors" de suítes de referência (HWiNFO). Cada fórmula
> do config vira uma série real de telemetria — `Custom#i` — avaliada a cada ciclo
> **no fim do snapshot, antes do commit das estatísticas**; logo min/máx/média da
> sessão, CSV/JSON, SDK de telemetria, HTTP, inventário, OSD e alertas a enxergam
> exatamente como uma série nativa.

## Config

No Avalonia/Linux, a página **Sensores customizados** permite criar, editar,
remover e reordenar essas entradas sem editar o JSON manualmente. O editor valida
nomes obrigatórios e únicos, sintaxe das fórmulas e o limite de 64 sensores antes
de salvar. A configuração continua usando exatamente o schema abaixo, também
consumido pelo WPF.

Em `config.json` (junto a `sensors`/`profiles`):

```json
"customSensors": [
  { "name": "DeltaTemp",  "formula": "{GpuTemperature} - {CpuTemperature}", "unit": "°C" },
  { "name": "VramShare",  "formula": "{GpuMemoryUsed} / {GpuMemoryTotal} * 100", "unit": "%" },
  { "name": "PicoGpu",    "formula": "max({GpuTemperature#1}, {GpuTemperature#2})" },
  { "name": "OverAmbient","formula": "{DeltaTemp} - 22" }
]
```

Campos:

| Campo | Descrição |
|---|---|
| `name` | Nome amigável (obrigatório). Vira o rótulo da série e a referência `{Nome}` em outras fórmulas/linhas do OSD |
| `formula` | Expressão derivada de outras séries (obrigatório; fórmulas inválidas são descartadas com nota em `logs/errors.log`) |
| `unit` | Opcional. Só compõe o rótulo exibido (`"DeltaTemp (°C)"`) |

A série resultante é `Custom#i`, em que `i` é a **posição da fórmula no config**
(a partir de 1). O índice é estável enquanto a ordem/lista não mudar — alertas
podem mirar `sensor: "Custom"` + `sensorIndex: 2`.

## Sintaxe da fórmula

- **Operadores**: `+ - * /` com precedência padrão e parênteses; unário `-` (o parser também aceita `+` unário, que é descartado).
- **Números**: decimais com **ponto** (`0.5`); vírgula é separador de argumentos.
- **Funções**: `min(a, b, ...)`, `max(a, b, ...)` (variádicas) e `abs(a)`.
- **Referências** `{...}` (seguinte resolução, similar às templates do OSD):
  1. **Chave explícita** — `GpuTemperature` (canônica) ou `GpuTemperature#1`
     (instância). Canônica ausente cai na **primeira instância publicada**.
  2. **Padrão wildcard** — `GpuTemperature#*`, `FanSpeed#*`: o primeiro match na
     ordem canônica→instâncias.
  3. **Nome amigável** — `{DeltaTemp}` casa o rótulo (igual exato, sem diferenciar maiúsculas/minúsculas, ou nome-base
     antes de " (": a unidade "DeltaTemp (°C)" também casa). Isso permite fórmulas
     referenciando **outros sensores customizados pelo nome** (avaliação na ordem
     do config; fórmulas posteriores enxergam as anteriores do mesmo ciclo).

## Semântica de ausência

Ciclo em que **qualquer referência não resolve** ou o **resultado não é finito**
(divisão por zero, NaN, ±∞): o sensor não publica valor — a série some do CSV/
SDK/inventário/OSD até voltar a resolver (mesmo contrato "linhas escondidas até
resolverem" das templates do OSD). Umbrais e estatísticas de sessão ignoram o
ciclo ausente.

## Onde a série aparece

- **Inventário** (aba "Séries publicadas") como `Custom#1`, `Custom#2`... com o
  rótulo configurado — é a forma mais fácil de descobrir o índice para alertas.
- **OSD**: adicione `"Custom#*"` (ou `Custom#1`...) à `overlay.shownSeries`, ou
  use linhas de template com `{DeltaTemp}` / `{DeltaTemp:2}` — o placeholder
  resolve pela chave ou pelo nome.
- **Alertas**: `sensor: "Custom"` + `sensorIndex` (regra "Above 90" na série
  derivada dispara como qualquer outra).
- **CSV/JSON/SDK/HTTP**: linha/coluna `Custom#i` com os mesmos rótulos — a
  fórmula não é exportada (só o valor); o `config.json` é a fonte da fórmula.

## Comportamento de runtime

- Fórmulas são compiladas **uma vez** (parser→AST com cota máx. de 256 nós) — a
  avaliação por ciclo é só resolução de referências + aritmética.
- Teto de **64 sensores customizados** por config.
- A engine é compartilhada entre Windows (`HardwareMonitorService`) e Linux
  (`LinuxSensorService`): fórmulas usam as séries que cada provedor publica
  (ex.: no Linux não há `PresentLatency`, mas há `NetworkDown`/`GpuCore`).
- No Avalonia/Linux, **Salvar e aplicar** persiste as alterações e recria o
  serviço imediatamente. Se a coleta estiver rodando, ela reinicia; se pausada,
  volta pausada; se parada, continua parada. A recriação zera as estatísticas da
  sessão. No WPF, o formato permanece compatível e a engine é criada junto ao
  serviço de sensores.
- A ordem determina o índice `Custom#i` e a ordem de avaliação. Uma fórmula só
  pode referenciar sensores customizados anteriores. Reordenar pode mudar os
  índices usados por alertas, por isso o editor mostra um aviso antes de salvar.
- A validação da tela verifica a sintaxe, não a presença atual de cada sensor
  referenciado. Se uma série física estiver ausente, ou o resultado não puder ser
  calculado naquele ciclo, a série customizada fica sem leitura até resolver.

## Limitações (documentadas de propósito)

- Não há operador `%`/expoente/condicionais; não há referência a "séries
  históricas" (buffer de frametime). Condições podem ser emuladas com `min`/`max`
  — ex. saturar CPU a 100: `min({CpuPerCore} + 10, 100)`.
- A unidade não participa do cálculo (rótulo apenas).
