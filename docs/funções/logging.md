# Logging (CSV / JSON)

Sistema de logging para exportação dos dados de sensores em formato CSV e/ou JSON.

## CsvLogger

Salva os dados de sensores em arquivo CSV no diretório `logs/`.

- Cada sensor vira uma coluna no CSV
- Cria um novo arquivo a cada execução com timestamp no nome
- Formato: `sensors_YYYYMMDD_HHmmss.csv`

## JsonLogger

Salva o snapshot mais recente em um arquivo JSON no diretório `logs/`.

- Sempre sobrescreve o mesmo arquivo (`sensors_latest.json`)
- Escrita **atômica** (`.tmp` + substituição): leitores externos nunca veem JSON truncado
- Formato JSON legível com todos os valores de sensores
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
