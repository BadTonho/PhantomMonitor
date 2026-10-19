# FpsMonitor

Monitor de FPS independente de alta precisão que utiliza o **Intel PresentMon** (open-source) em segundo plano para capturar eventos de apresentação de frames (`Present`) da placa de vídeo via ETW nativo. Não depende de MSI Afterburner ou RTSS e é imune a banimentos de anti-cheat por não injetar DLL nos jogos.

## Funcionalidades

- Leitura de FPS e Frametime (ms) em tempo real da janela/jogo em primeiro plano
- Histórico circular de frametime para gráficos em tempo real (600 amostras)
- Estatísticas dinâmicas: FPS instantâneo (com suavização EMA), médio, mínimo, máximo, 1% Low e 0.1% Low (janela móvel de ~3600 frames, ≈60 s a 60 FPS)
- Detecção automática de jogos em primeiro plano (ignora explorer, tarefas do sistema e a própria janela do PhantomMonitor)
- Encerramento automático e limpo do processo do PresentMon ao fechar o app

## Arquitetura & Integração

- **Contrato Multiplataforma (`IFpsMonitor`):** Interface unificada que abstrai a captura de taxas de quadros entre Windows (`FpsMonitor`) e Linux (`LinuxFpsMonitor`).
- **Motor Windows:** `PresentMon.exe` (binário standalone oficial da Intel, armazenado em `tools/` e copiado automaticamente para a pasta de saída do build)
- **Motor Linux:** `PhantomVulkanLayer` (camada Vulkan nativa em C++ que intercepta `vkQueuePresentKHR` e transmite dados via memória compartilhada `/dev/shm/phantom_fps_<pid>`, dispensando softwares como MangoHud)
- **Comunicação no Windows:** o serviço C# inicializa o processo com saída padrão redirecionada (`--output_stdout --no_console_stats --stop_existing_session --session_name PhantomMonitorFps --exclude PhantomMonitor.exe --no_track_input --no_track_gpu --exclude_dropped`) e analisa o CSV em tempo real. Os argumentos precisam existir no PresentMon 2.x (por exemplo, `--no_track_gpu_video` **não existe** e faz o processo encerrar imediatamente).
- **Parse Dinâmico:** mapeia as colunas do PresentMon (`ProcessID`, `Application`, `msBetweenPresents` / `FrameTime`) e trata campos CSV entre aspas
- **Detecção de Foco:** `GetForegroundWindow` e `GetWindowThreadProcessId` via P/Invoke identificam o jogo ativo a cada 500ms
- **Privilégios:** o PresentMon exige Administrador (ou o grupo "Performance Log Users"). Sem isso, `EtwDiag` mostra "Requer Administrador" e o motivo completo vai para `logs/errors.log`

## Propriedades

- `Fps` - FPS atual (suavizado via Exponential Moving Average)
- `FrametimeMs` - Frametime em milissegundos
- `FpsAvg` - FPS médio da janela móvel de 3600 amostras (~60 s a 60 FPS)
- `FpsMin` - Menor FPS da janela recente (atualizado a cada ~1 s)
- `Fps1Low` - 1% Low (índice de 1% das amostras ordenadas; pior 1% para detecção de stuttering)
- `Fps01Low` - 0.1% Low (índice de 0.1%; mais rigoroso que o 1% Low e sensível a engasgos isolados)
- `FpsMax` - Maior FPS da janela móvel
- `HasData` - Indica se há dados de frames ativos
- `FrametimeHistory` - Buffer circular de 600 amostras usado pelo gráfico do overlay
- `DebugInfo` / `PerfResult` / `EtwDiag` - Diagnósticos e status da sessão do PresentMon

## Métricas extras do PresentMon e percentis puros (P6.5)

Além do FPS/frametime clássicos, o monitor consome **colunas opcionais** do CSV do
PresentMon 2.x e publica percentis brutos:

| Propriedade / série | Unidade | Fonte / conteúdo |
|---|---|---|
| `GpuBusy` | % | Coluna `GpuBusy%`/`GPU Busy` (ocupação real da GPU durante o frame) |
| `CpuBusy` | % | Coluna `CpuBusy%` (custo de CPU da pilha de present) |
| `CpuWait` | % | Coluna `CpuWait%` (CPU ociosa esperando present) |
| `PresentLatencyMs` | ms | Coluna `MsUntilDisplayed`/`PresentLatency` (present → display) |
| `FpsP1` / `FpsP99` | FPS | Percentil 1/99 **bruto** da janela de ~60 s de FPS |
| `FrametimeP1` / `FrametimeP99` | ms | Percentil 1/99 da janela de frametimes (P99 expõe os spikes do gráfico) |

Detalhes de robustez:

- **Casamento por substring case-insensitive:** o nome dessas colunas varia
  entre versões do PresentMon ("GpuBusy%", "GPU Busy %", "MsUntilDisplayed"...).
- **Coluna ausente = 0 ("indisponível"):** versões antigas do executável ou flags
  que desativam o tracking publicam 0 nas séries; os consumidores tratam `<= 0`
  como indisponível (mesmo contrato das temperaturas).
- **Valores plausíveis:** busy/wait são aceitos em 0–100% e latência em 0–2000 ms —
  lixo de driver não entra nas janelas de estatística.
- **Percentis:** calculados no tick de ~1 s sobre a mesma janela de 3600 amostras
  do Avg/Min; o `FpsP1` coincide com o `Fps1Low` já existente (mesmo índice da
  janela), e as séries novas ganham as counterparts P99 e os percentis de frametime
  que antes só existiam visualmente no gráfico. Janelas independentes para FPS e frametime.
