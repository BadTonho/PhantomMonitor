# FrametimeBuffer

Buffer circular para armazenamento do histórico de frametime. Utilizado pelo FpsMonitor para manter as últimas N amostras de frametime para exibição do gráfico e cálculo de estatísticas.

## Funcionalidades

- Armazenamento circular com capacidade configurável (padrão: 600 amostras)
- Adição de novos valores sobrescrevendo os mais antigos automaticamente
- Exportação para array ordenado cronologicamente
- Cálculo de estatísticas: mínimo, máximo e média

## Principais métodos

- `Add(float value)` - Adiciona um novo valor ao buffer
- `Clear()` - Limpa todo o buffer
- `ToArray()` - Retorna um array com os valores em ordem cronológica
- `GetStats()` - Retorna tupla (min, max, avg) dos valores armazenados

## Propriedades

- `Capacity` - Capacidade máxima do buffer
- `Count` - Número atual de amostras
- `Data` - Span com os dados brutos
