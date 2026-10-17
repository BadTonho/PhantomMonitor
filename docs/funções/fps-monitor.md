# FpsMonitor

Monitor de FPS independente de alta precisão que utiliza o **Intel PresentMon** (open-source) em segundo plano para capturar eventos de apresentação de frames (`Present`) da placa de vídeo via ETW nativo. Não depende de MSI Afterburner ou RTSS e é imune a banimentos de anti-cheat por não injetar DLL nos jogos.

## Funcionalidades

- Leitura de FPS e Frametime (ms) em tempo real da janela/jogo em primeiro plano
- Histórico circular de frametime para gráficos em tempo real (600 amostras)
- Estatísticas dinâmicas: FPS instantâneo (com suavização EMA), FPS médio, mínimo e 1% Low
- Detecção automática de jogos em primeiro plano (ignora explorer, tarefas do sistema e a própria janela do RivaMonitor)
- Encerramento automático e limpo do processo do PresentMon ao fechar o app

## Arquitetura & Integração

- **Motor:** `PresentMon.exe` (standalone oficial v2.6.0 da Intel, armazenado em `tools/` e copiado automaticamente para a pasta de saída)
- **Comunicação:** O serviço C# inicializa o processo com saída padrão redirecionada (`--output_stdout --no_console_stats`) e analisa o fluxo de CSV em tempo real de forma assíncrona
- **Parse Dinâmico:** Mapeia automaticamente as colunas do PresentMon (`ProcessID`, `Application`, `msBetweenPresents` / `FrameTime`)
- **Detecção de Foco:** `GetForegroundWindow` e `GetWindowThreadProcessId` via P/Invoke identificam o jogo ativo a cada 500ms

## Propriedades

- `Fps` - FPS atual (suavizado via Exponential Moving Average)
- `FrametimeMs` - Frametime em milissegundos
- `FpsAvg` - FPS médio dos frames recentes
- `FpsMin` - Menor FPS registrado na sessão atual
- `Fps1Low` - 1% Low (pior 1% dos frametimes para detecção de stuttering)
- `HasData` - Indica se há dados de frames ativos
- `FrametimeHistory` - Buffer circular de 600 amostras usado pelo gráfico do overlay
- `DebugInfo` / `PerfResult` / `EtwDiag` - Diagnósticos e status da sessão do PresentMon
