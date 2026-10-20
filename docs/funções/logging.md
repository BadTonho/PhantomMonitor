# Logging (CSV / JSON)

Sistema de logging para exportação dos dados de sensores em formato CSV e/ou JSON.

## CsvLogger

Salva os dados de sensores em arquivo CSV no diretório `logs/`.

- Colunas **dinâmicas**: o cabeçalho é escrito na primeira escrita com as séries
  presentes no primeiro snapshot — coluna 0 = `Timestamp`, depois as séries
  canônicas (na ordem do enum `SensorType`) e por fim as séries por instância
  (`GpuCore#1`, `CpuTemperature#3`, `FanSpeed#2`...), ver [sensor-key / hardware-monitor](hardware-monitor.md)
- Cria um novo arquivo a cada execução com timestamp no nome
- Hardware que desaparece depois (USB desligada, SSD removido) mantém sua coluna
  na tabela; a célula fica em zero — o alinhamento de planilhas não quebra
- Formato: `sensors_YYYYMMDD_HHmmss.csv`

## JsonLogger

Salva o snapshot mais recente em um arquivo JSON no diretório `logs/`.

- Sempre sobrescreve o mesmo arquivo (`sensors_latest.json`)
- Escrita **atômica** (`.tmp` + substituição): leitores externos nunca veem JSON truncado
- Formato JSON legível com todos os valores de sensores
- Séries por instância aparecem em `series` / `seriesLabels` com chaves `Categoria#índice`
  (ex.: `series: { "GpuCore#1": 65.0 }`); as séries canônicas continuam em `values`
- `seriesStats` traz as estatísticas da sessão com a MESMA chave `Categoria#índice`
  (`min`, `max`, `average`, `count`); só aparecem séries com ao menos uma amostra
- Útil para integração com outras ferramentas

## Marcadores no CSV (`#marker`)

O `CsvLogger.WriteMarker()` insere uma linha `#marker,<timestamp>,<rótulo>` no
log ativo — delimita segmentos de sessão/benchmark sem quebrar planilhas: quem
consome só as colunas numéricas ignora a linha. Na prática é gerado pelo
**reset de estatísticas** (bandeja/inventário) com o rótulo `reset (<origem>)`.
Só tem efeito com o CSV já aberto (a partir da primeira linha de valores).

## JsonJournalLogger (journal JSON contínuo, JSONL)

Journal **append-only** em `logs/sensors_YYYYMMDD_HHmmss.jsonl`: **uma linha por
evento**, custo de gravação constante (nada é reescrito a cada ciclo, diferente
do snapshot sensors_latest.json) — indicado para benchmarks longos e replay.

```json
{"kind":"meta","ts":"...","labels":{"GpuCore":"GPU Core (Usage)","Custom#1":"DeltaTemp (°C)"}}
{"kind":"sample","ts":"...","series":{"GpuCore":12.3,"GpuCore#1":8.1,"Custom#1":7.7}}
{"kind":"note","ts":"...","text":"reset (bandeja)"}
```

- **`meta`**: única linha de cabeçalho (na primeira amostra) com o mapa
  série→rótulo do ciclo; amostras seguintes carregam só chave→valor.
- **`sample`**: uma por snapshot com TODAS as séries presentes (canônicas +
  por instância + `Custom#i`).
- **`note`**: marcadores da sessão — **início** ("início da sessão") e **resets
  de estatísticas** (`"reset (<origem>)"`, mesmos gatilhos do `#marker` do CSV).
- Escrita UTF-8 com flush imediato; qualquer falha de I/O cai no `errors.log`
  (`JsonJournal.Write`) e nunca derruba o monitoramento.
- Consumo típico: `Get-Content -Wait logs\sensors_*.jsonl -Tail 1` (tail
  client) ou `jq -s` sobre o arquivo fechado no fim da sessão.

## ErrorLog (`logs/errors.log`)

Registro de erros internos (substitui os antigos `catch` vazios). Fica em `logs/` ao lado do executável.

- Formato: `data hora [contexto] mensagem`
- Mensagens idênticas consecutivas são suprimidas; o arquivo é zerado ao passar de ~1 MB
- Nunca lança exceção (falha de log não derruba o app)

Também é o destino de todos os tratadores globais de exceção do app:
- `Dispatcher.UnhandledException` — falhas na UI thread (modal só é aberto para erros diferentes do anterior; repetidos ficam apenas no log)
- `AppDomain.UnhandledException` — encaminha erros de threads de fundo
- `TaskScheduler.UnobservedTaskException` — exceções não observadas em tasks (marcadas como observadas)

## Ativação

Controlado pelo `config.json`:
- `logToCsv` - Ativar logging CSV
- `logToJson` - Ativar logging JSON
- `logToJsonJournal` - Ativar journal JSON contínuo (JSONL)

Também pode ser ativado via checkboxes na sidebar da interface principal.
