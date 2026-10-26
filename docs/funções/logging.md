# Logging (CSV / JSON)

> **Idioma:** Português (Brasil) · [English](../en/functions/logging.md)

Sistema de logging para exportação dos dados de sensores em formato CSV e/ou JSON.
Os formatos e estruturas são compartilhados pelas plataformas no app único; a
pasta de destino é a de dados por usuário do sistema
(`%LOCALAPPDATA%\PhantomMonitor\logs` no Windows, `~/.local/share/PhantomMonitor/logs`
no Linux).

## CsvLogger

Salva os dados de sensores em arquivo CSV. No app único, usa o diretório de logs
do usuário: `%LOCALAPPDATA%\PhantomMonitor\logs\` no Windows e
`~/.local/share/PhantomMonitor/logs/` no Linux (respeitando `XDG_DATA_HOME`
quando definido).

- Colunas **dinâmicas**: o cabeçalho é escrito na primeira escrita com as séries
  presentes no primeiro snapshot — coluna 0 = `Timestamp`, depois as séries
  canônicas (na ordem do enum `SensorType`) e por fim as séries por instância
  (`GpuCore#1`, `CpuTemperature#3`, `FanSpeed#2`...), ver [sensor-key / hardware-monitor](hardware-monitor.md)
- Cria um novo arquivo a cada início de monitoramento, com timestamp no nome
- O timestamp da sessão inclui milissegundos para evitar colisão ao
  parar e iniciar novamente dentro do mesmo segundo
- Hardware que desaparece depois (USB desligada, SSD removido) mantém sua coluna
  na tabela; a célula fica vazia — o alinhamento de planilhas não quebra
- Formato: `sensors_YYYYMMDD_HHmmss_fff.csv` (o formato antigo do WPF aposentado,
  `_YYYYMMDD_HHmmss.csv`, permanece legível por quem consome as colunas)

## Rotação do CSV e limite de escrita

Quando surge uma série nova (por exemplo, FPS depois do início), o CSV abre
`.part002.csv`, `.part003.csv` etc. com a união das colunas. Os segmentos
anteriores permanecem intactos; consumidores devem ler todos os segmentos.

A fila de escrita é limitada a 256 snapshots. Se o disco não acompanhar a
coleta, novas amostras são descartadas com diagnóstico no `ErrorLog` e contador
`DroppedSnapshots`, preservando a ordem das amostras aceitas e a memória do app.

## JsonLogger

Salva o snapshot mais recente em `sensors_latest.json` no diretório de logs do
app: `%LOCALAPPDATA%\PhantomMonitor\logs\` no Windows ou
`~/.local/share/PhantomMonitor/logs/` no Linux.

- Sempre sobrescreve o mesmo arquivo (`sensors_latest.json`)
- Escrita **atômica** (`.tmp` + substituição): leitores externos nunca veem JSON truncado
- Formato JSON legível com todos os valores de sensores
- Séries por instância aparecem em `series` / `seriesLabels` com chaves `Categoria#índice`
  (ex.: `series: { "GpuCore#1": 65.0 }`); as séries canônicas continuam em `values`
- `seriesStats` traz as estatísticas da sessão com a MESMA chave `Categoria#índice`
  (`min`, `max`, `sum`, `count`, `average`); só aparecem séries com ao menos uma amostra
- `monitors` traz a identidade dos monitores conectados (EDID via WMI, cache de ~60 s):
  fabricante, modelo, código de produto, nº de série, diagonal em polegadas,
  ano/semana de fabricação e flag de monitor primário — os processos da GPU NÃO
  entram no snapshot (são exibidos apenas na página Inventário)
- Útil para integração com outras ferramentas

## Marcadores no CSV (`#marker`)

O `CsvLogger.WriteMarker()` insere uma linha `#marker,<timestamp>,<rótulo>` no
log ativo — delimita segmentos de sessão/benchmark sem quebrar planilhas: quem
consome só as colunas numéricas ignora a linha. Na prática é gerado pelo
**reset de estatísticas** (bandeja/inventário) com o rótulo `reset (<origem>)`.
Só tem efeito com o CSV já aberto (a partir da primeira linha de valores).

A gravação de CSV/JSON/journal acontece em uma thread dedicada de drain (ordem
FIFO dos snapshots preservada), então escritas demoradas em disco não travam a
interface durante o monitoramento.

## JsonJournalLogger (journal JSON contínuo, JSONL)

Journal **append-only** em `sensors_YYYYMMDD_HHmmss_fff.jsonl` (milissegundos; o
formato antigo do WPF `_YYYYMMDD_HHmmss.jsonl` permanece legível) no diretório de
logs do app: `%LOCALAPPDATA%\PhantomMonitor\logs\` no Windows ou
`~/.local/share/PhantomMonitor/logs/` no Linux. Registra **uma linha por
evento**, custo de gravação constante (nada é reescrito a cada ciclo, diferente
do snapshot sensors_latest.json) — indicado para benchmarks longos e replay.

```json
{"kind":"meta","ts":"...","labels":{"GpuCore":"GPU Core (Usage)","Custom#1":"DeltaTemp (°C)"}}
{"kind":"sample","ts":"...","series":{"GpuCore":12.3,"GpuCore#1":8.1,"Custom#1":7.7}}
{"kind":"note","ts":"...","text":"reset (bandeja)"}
```

- **`meta`**: única linha de cabeçalho (na primeira amostra) com o mapa
  série→rótulo do ciclo; amostras seguintes carregam só chave→valor. Por vir
  DEPOIS da nota de início de sessão (gravada na abertura do journal), a linha
  `meta` não é necessariamente a primeira do arquivo (o arquivo abre com a
  nota "início da sessão").
- **`sample`**: uma por snapshot com TODAS as séries presentes (canônicas +
  por instância + `Custom#i`).
- **`note`**: marcadores da sessão — **início** ("início da sessão") e **resets
  de estatísticas** (`"reset (<origem>)"`, mesmos gatilhos do `#marker` do CSV).
- Escrita UTF-8 com flush imediato; qualquer falha de I/O cai no `errors.log`
  (`JsonJournal.Write`) e nunca derruba o monitoramento.
- Consumo típico: `Get-Content -Wait logs\sensors_*.jsonl -Tail 1` (tail
  client) ou `jq -s` sobre o arquivo fechado no fim da sessão.

## ErrorLog (`errors.log`)

Registro local de falhas e eventos de diagnóstico. No Windows fica em `%LOCALAPPDATA%\PhantomMonitor\logs\` (o WPF antigo gravava `logs/` ao lado do executável; o app unificado sempre usa a pasta de dados do usuário). No Linux, fica na pasta de dados do usuário (`~/.local/share/PhantomMonitor/logs/`, respeitando `XDG_DATA_HOME`), junto aos logs de telemetria.

- Formato: `data hora [contexto] mensagem`
- Mensagens idênticas consecutivas são suprimidas; o arquivo é zerado ao passar de ~1 MB
- Nunca lança exceção (falha de log não derruba o app)
- O centro **Diagnóstico** exibe até 500 registros recentes, atualiza quando novas mensagens chegam e oferece abrir o arquivo no aplicativo padrão do sistema
- Em caso de falha ao escrever o arquivo, a ocorrência pode permanecer visível na lista em memória durante a execução atual

O app único registra exceções não tratadas da UI, do domínio da aplicação e de tarefas não observadas. Falhas inesperadas dos leitores Linux também são registradas com contexto; arquivos opcionais ausentes continuam sendo tratados como informação de disponibilidade.

## Ativação

Controlado pelo `config.json`, carregado quando o app inicia:
- `logToCsv` - Ativar logging CSV
- `logToJson` - Ativar logging JSON
- `logToJsonJournal` - Ativar journal JSON contínuo (JSONL)

No app único, essas opções são lidas ao iniciar o app e aplicadas quando a
coleta inicia; também podem ser editadas na página **Configurações**, que salva
no `config.json` e aplica os novos loggers sem interromper o dashboard (o
comportamento herdado do WPF aplicava na próxima sessão de monitoramento).
Pausar a coleta interrompe temporariamente a
entrada de novas amostras; parar ou fechar o app fecha os arquivos ativos.
