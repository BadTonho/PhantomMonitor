# SensorFormatter

Utilitário estático de formatação de métricas compartilhado entre a janela principal (cards) e o overlay OSD. Centraliza o estilo de exibição para que as duas superfícies mostrem os mesmos formatos, evitando duplicação de lógica.

## Funções

- `FormatBytes(bytes)` → `1.5 KB`, `12.3 MB`, `1.20 GB`
- `FormatBytesPerSec(bytes)` → mesma escala com sufixo `/s` (disco e rede)
- `FormatClock(mhz)` → `850 MHz` / `4.15 GHz`
- `FormatFan(value)` → `--`, `45%` (valores ≤ 100, GPUs NVIDIA) ou `1450 RPM` (SuperIO/AMD)
- `BuildCpuDetail(data)` → linha compacta com clock, potência e voltagem disponíveis (`null` quando não há nenhum)
- `BuildGpuDetail(data)` → linha compacta com VRAM usada/total, clock, potência e hotspot (`null` quando não há nenhum)
- `BuildFanSummary(data)` → resumo de ventoinhas para os cards (`Ventoinhas: GPU 45% | Sistema 1200 RPM`)

## Consumidores

- `MainWindow` — cards de CPU/GPU (linhas de detalhes), Rede e Placa-mãe & Ventoinhas
- `OverlayWindow` — sublinhas de detalhes de CPU/GPU e linhas `NET`/`FAN`
