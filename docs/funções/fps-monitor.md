# FpsMonitor

Monitor de FPS independente que utiliza a API nativa do Windows (`DwmGetWindowAttribute`) para ler o timestamp de apresentação de frames da janela em primeiro plano. Não depende de nenhum software externo (MSI Afterburner, RTSS, etc).

## Funcionalidades

- Leitura de FPS em tempo real da janela ativa
- Cálculo de frametime (ms) baseado no intervalo entre frames
- Histórico de frametime para gráficos (600 amostras)
- Estatísticas: FPS médio, mínimo e 1% low
- Detecção automática de janelas de jogos (DirectX)

## Principais métodos

- `Poll()` - Deve ser chamado a cada ciclo de atualização. Verifica a janela em primeiro plano e conta frames
- `Reset()` - Reseta todas as estatísticas e histórico

## API utilizada

- `DwmGetWindowAttribute` com `DWMWA_LAST_FRAME_PRESENT_TIME` (Windows 10+)
- `GetForegroundWindow` para detectar a janela ativa
- `QueryPerformanceCounter`/`QueryPerformanceFrequency` para temporização precisa

## Propriedades

- `Fps` - FPS atual
- `FrametimeMs` - Frametime em milissegundos
- `FpsAvg` - FPS médio (últimos 2 segundos)
- `FpsMin` - FPS mínimo registrado
- `Fps1Low` - 1% low (pior 1% dos frames)
- `HasData` - Indica se há dados de FPS disponíveis
- `FrametimeHistory` - Buffer circular com histórico de frametimes
- `DebugInfo` - String com informações de depuração
