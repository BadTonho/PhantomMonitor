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
- Útil para integração com outras ferramentas
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

Também pode ser ativado via checkboxes na sidebar da interface principal.
