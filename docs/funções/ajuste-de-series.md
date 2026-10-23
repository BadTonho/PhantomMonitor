# Cadência de amostragem e suavização por série (P9.1)

> **Idioma:** Português (Brasil) · [English](../en/functions/series-tuning.md)

Ajuste fino **por série** do ritmo de publicação e do filtro de valor, no espírito
do "polling por sensor + smoothing" das suítes de referência. A leitura física
continua no tick global (`updateIntervalMs`); a camada de ajuste decide, série por
série, quando o valor publicado pode se refrescar e com qual média móvel ele
atravessa o jitter.

## Config

```json
"sensorTuning": [
  { "series": "CpuTemperature", "intervalMs": 2000, "smoothing": 3 },
  { "series": "WheaErrorCount", "intervalMs": 30000 },
  { "series": "CpuTemperature#*", "intervalMs": 2000 },
  { "series": "Custom#1", "smoothing": 5 }
]
```

| Campo | Significado |
|---|---|
| `series` | Padrão no formato `SensorKey` (mesmos casamentos da whitelist do OSD): canônica exata (`"GpuTemperature"`), instância (`"GpuTemperature#1"`), categoria de instância (`"CpuTemperature#*"`), família (`"Gpu*"` — canônica **e** instâncias), sensor customizado (`"Custom#1"`). Sem distinção de maiúsculas |
| `intervalMs` | Cadência de refresh da série em ms (100–3.600.000). `0` = publicar toda amostra do tick global (padrão). O `Normalize()` sobe o valor quando ele fica **abaixo do tick global** (intervalo menor seria no-op) |
| `smoothing` | Janela da média móvel sobre as **amostras frescas** (2–60). `0`/`1` = desligado (padrão) |

- Teto de **64 regras** (mesma política das `customSensors`); regra sem `series` é descartada.
- **Várias regras casando a mesma série:** a ÚLTIMA da lista vence — cadência,
  suavização e marca de estatística são todas da última (uma regra
  sempre-fresta mais nova cancela a repetição de uma anterior).

## Semântica

1. **Repetição entre amostras:** dentro do intervalo configurado, a série repete
   o último valor publicado — a leitura vale "a partir da última amostragem".
   CSV/JSON/SDK/journal registram o valor constante por tick (timeline), e o
   OSD/inventário exibem o valor estável entre refreshes.
2. **Estatísticas amostram na cadência da série:** o `SeriesStatsTracker` **pula**
   as amostras repetidas (parâmetro novo do `Commit`). Sem isso, o tick global
   duplicaria amostras e "amansaria" min/máx/média — a média da sessão conta só
   leituras frescas, na cadência configurada.
3. **Suavização:** média móvel da janela das amostras frescas, publicada no
   lugar do bruto. Buffers circulares por série (`FrametimeBuffer`, mesma classe
   do gráfico de frametime/sparklines); valores não finitos não entram na janela.
4. **Primeira amostra sempre fresca:** estado vazio → publica e armar o relógio;
   série que desaparece e volta não "repete fantasma" (série ausente é ignorada
   sem criar estado).
5. **Ordem no ciclo:** sensores customizados (P8.1) avaliam **antes** do ajuste
   (fórmulas enxergam a leitura crua), depois o tuning trata todas as séries —
   incluindo `Custom#i` como série comum (uma fórmula ruidosa pode levar
   `"smoothing"` na própria regra).

## Consumidores

- **Pipeline:** aplicado no `HardwareMonitorService` (Windows) e no
  `LinuxSensorService` (Linux), sempre antes do commit das estatísticas —
  firmware único via `SensorTuningEngine` (criação em `SensorServiceFactory`).
- **Alertas:** regra num sensor com `intervalMs` alto dispara com o atraso da
  cadência (ex.: `intervalMs: 30000` = alerta até 30 s depois do limiar cru).
- **Gráficos do OSD (P8.5):** plotam o valor publicado — séries com cadência
  lenta mostram platôs naturais entre refreshes.

## Cuidados

- **Contadores e somatórios** (`DiskTotalRead/Written`, `NetworkUpTotal/DownTotal`)
  **não devem ser suavizados** (média de total acumulado não tem significado
  físico). A cadência também não ajuda nesses: eles integram no tick global.
- **FPS/percentis do PresentMon** já têm janela móvel (~60 s) e **EMA própria**
  (α = 0,35) no `FpsMonitor` — não duplicar com `smoothing`.
- O engine não distingue natureza da série: cabe ao config não suavizar o que
  não é leitura instantânea.

## Página Ajuste por série

No app único (Windows e Linux), a página **Ajuste por série** edita a lista `sensorTuning` sem edição
manual do JSON. É possível selecionar uma série detectada no inventário, manter
um alvo já salvo mesmo quando está ausente no momento, ou digitar uma chave ou
padrão manual como `Gpu*` e `CpuTemperature#*`. Padrões sem correspondência são
preservados para séries que apareçam depois.

O editor aceita até 64 regras. `intervalMs` pode ser `0` (tick global) ou um
valor positivo entre 100 e 3.600.000 ms; intervalos positivos abaixo de
`updateIntervalMs` são elevados ao tick global. `smoothing` aceita 0–60; 0 e 1
desativam a média móvel e 2–60 definem sua janela. A interface permite subir e
descer regras. Como no engine, a última regra correspondente vence quando há
sobreposição.

**Salvar e aplicar** grava com `AppConfig.Save()` e recria o serviço de coleta para
usar as regras novas. Uma falha de gravação preserva a configuração em execução.
A coleta parada continua parada; a coleta pausada continua pausada. Quando a
coleta está ativa, a recriação inicia uma nova sessão de sensores e zera suas
estatísticas. O mesmo bloco `sensorTuning` do `config.json` é consumido pelo app
único nos dois sistemas (o WPF aposentado usava o mesmo bloco, sem mudança de
formato).
