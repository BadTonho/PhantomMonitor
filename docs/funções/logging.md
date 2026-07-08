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
- Formato JSON legível com todos os valores de sensores
- Útil para integração com outras ferramentas

## Ativação

Controlado pelo `config.json`:
- `logToCsv` - Ativar logging CSV
- `logToJson` - Ativar logging JSON

Também pode ser ativado via checkboxes na sidebar da interface principal.
