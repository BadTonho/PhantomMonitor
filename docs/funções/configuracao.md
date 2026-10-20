# AppConfig / Config

Sistema de configuração baseado em JSON. O arquivo `config.json` fica na raiz do projeto e é carregado automaticamente.

## Modelos

### AppConfig
- `updateIntervalMs` - Intervalo de atualização em milissegundos (padrão: 1000)
- `logToCsv` - Habilitar logging em CSV
- `logToJson` - Habilitar logging em JSON
- `logToJsonJournal` - Journal JSON contínuo (JSONL): uma linha por amostra em `logs/sensors_*.jsonl` + notas nos marcadores de sessão (início/resets) — ver [logging.md](logging.md)
- `showOverlay` - Mostrar overlay na inicialização
- `toggleOverlayHotkey` - Atalho global para mostrar/ocultar o OSD (ex: "Ctrl+Shift+O"; vazio = desativado)
- `pauseMonitorHotkey` - 2º atalho global (P6.7): pausa/retoma o monitoramento (padrão `Ctrl+Alt+P`; vazio = desativado; estado de pausa não persiste) — ver [hotkey.md](hotkey.md) e [hardware-monitor.md](hardware-monitor.md)
- `overlayOpacity` - Opacidade do overlay (0-1)
- `overlayPosition` - Posição inicial do overlay (X, Y)
- `Overlay` - Configurações específicas do overlay
- `Sensors` - Quais sensores estão habilitados
- `alerts` - Regras de alerta com ações (popup/som/script/desligar); a página Avalonia edita limites, cooldown e notificação interna — ver [alertas.md](alertas.md)
- `telemetryExport` - Exportador HTTP do SDK de telemetria: `{ enabled: false, port: 7317, listenAll: false }` — publica o último snapshot como JSON em `GET /values` (mesmo payload do `sensors_latest.json`) e `GET /health`; padrão é loopback (`listenAll: true` abre para a LAN e pode exigir firewall) — ver [telemetry-sdk.md](../telemetry-sdk.md)
- `profiles` - Perfis por aplicativo do OSD: `[{ name, appMatch, offsetX?, offsetY?, width?, fontSize?, opacity?, accentColor?, series?, visible? }]` — ver [overlay.md](overlay.md); `appMatch` vazio ou `*` casa com qualquer executável; `visible: false` esconde o OSD enquanto o perfil casa (`opacity` vazio/nulo usa a `overlayOpacity` global)
- `overlay.templates` - Linhas personalizadas do OSD: `[{ text, color? }]` com placeholders `{Chave}`/`{Chave:N}`/ wildcard; linha oculta até todos os placeholders resolverem; cor só via config — ver [overlay.md](overlay.md)
- `customSensors` - Sensores customizados do usuário: `[{ name, formula, unit? }]` — fórmulas derivadas de outras séries (`{GpuTemperature} - {CpuTemperature}`, funções `min/max/abs`, wildcard), publicadas como série `Custom#i` a cada ciclo (entra em stats/CSV/SDK/alertas) — ver [sensores-customizados.md](sensores-customizados.md)
- `sensorTuning` - Cadência de amostragem + suavização **por série** (P9.1): `[{ series, intervalMs?, smoothing? }]` — entre amostras a série repete o último valor publicado, as estatísticas pulam os pontos repetidos e a média móvel atravessa o jitter nos refreshes — ver [ajuste-de-series.md](ajuste-de-series.md)
- `overlay.graphs` - Sparklines por série no OSD: `[{ series, height?, window?, color?, min?, max? }]` — resolução igual às templates (chave/wildcard/nome), buffer circular por gráfico, eixo auto ou fixo; container fica antes das templates — ver [overlay.md](overlay.md) (P8.5)

### OverlayConfig
- `width` - Largura do overlay em pixels
- `showCpu/Gpu/Ram/Disk/Network/Fans/Motherboard/Fps/FrametimeGraph` - Visibilidade de cada seção (`showFrametimeGraph` controla a sparkline de frametime dentro da seção FPS)
- `compactMode` - Modo compacto
- `accentColor` - Cor de destaque (hex, ex: "#1E90FF")
- `shownSeries` - Whitelist (padrões wildcard) das séries **por instância** exibidas no OSD
  além das linhas canônicas: `"Gpu*"` (multi-GPU), `"FanSpeed*"` (ventoinhas extras),
  `"Disk*"` (R/W, temperatura e saúde por disco), `"CpuTemperature#*"`, `"CpuPerCore#*"`,
  `"CpuClock#*"` (telemetria por núcleo), `"VrmTemperature*"`, `"RailVoltage*"`,
  `"Network*"` (por interface + totais da sessão). `"*"` libera tudo. Padrão:
  `["Gpu*", "FanSpeed*", "Disk*"]`. Lista vazia/ausente no config volta ao padrão.
  Os padrões são conferidos por `SensorKey.MatchesPattern` (sem distinção de maiúsculas).

### SensorConfig
- `Cpu/Gpu/Ram/Disk/Network/Motherboard` - Sensores habilitados

## Carregamento e localização

O caminho é resolvido por `ConfigPaths.ConfigFile` (independente da pasta de saída do build):

1. **Desenvolvimento:** sobe a partir do executável até encontrar `PhantomMonitor.slnx` (ou `.sln`) e usa o `config.json` da raiz do repositório.
2. **Publicado/instalado:** usa `%AppData%\PhantomMonitor\config.json` no Windows e `$XDG_CONFIG_HOME/PhantomMonitor/config.json` no Linux (normalmente `~/.config/PhantomMonitor/config.json`).

Se o arquivo não existir, `AppConfig.Load` copia o `config.example.json` (da mesma pasta ou ao lado do executável — o `.csproj` o copia para a saída) e cria o diretório se necessário.

## Validação e persistência

- `AppConfig.Normalize()` corrige valores inválidos (intervalo mínimo de 100 ms, opacidade entre 0,1 e 1, `overlayPosition.x/y` em ±5000, objetos `null`).
- `AppConfig.Save()` grava de forma atômica (arquivo `.tmp` + substituição) e retorna `bool`; o Editor de Overlay avisa se não conseguiu salvar.
- Falhas de leitura/gravação são registradas em `logs/errors.log` (ver [logging.md](logging.md)).
- `showOverlay` define o estado inicial do toggle "Overlay".
- **Toggles persistentes:** os checkboxes da sidebar (`Overlay no Jogo`, `Exportar CSV`, `Exportar JSON`, `Journal JSON (jsonl)`) gravam o estado no `config.json` ao serem alternados — antes só mudavam o estado vivo entre sessões de monitoramento.
- `overlay.showNetwork`, `overlay.showFans` e `overlay.showMotherboard` já são consumidos pelo overlay (linhas NET/FAN/MB, com auto-revelação no primeiro dado real) e editáveis pelo Editor Visual.
- `overlay.compactMode`, `overlay.accentColor` e `overlay.fontSize` são aplicados ao overlay (espaçamento reduzido, cor de destaque e tamanho base da fonte) e editáveis no Editor Visual.
- `toggleOverlayHotkey` registra um atalho global (Win32 `RegisterHotKey`) que alterna o OSD de qualquer aplicativo — ver [hotkey.md](hotkey.md). Editável na seção "ATALHO GLOBAL" do Editor Visual.
- `sensors.*` está reservado no modelo, mas ainda não é consumido pela interface.
- No Avalonia Linux, a página **Configurações** edita `updateIntervalMs` (100–60.000 ms), `logToCsv`, `logToJson` e `logToJsonJournal`. O diretório de logs é exibido como somente leitura.
- No Avalonia Linux, a página **Alertas** edita regras do bloco `alerts`; som, scripts e desligamento já existentes são preservados, mas não executados nesse frontend.
- **Aplicação no Linux:** salvar persiste com `AppConfig.Save()`. Alterar o intervalo recria o serviço de sensores, zera as estatísticas da sessão e preserva coleta pausada/parada. Alterar apenas os formatos de log atualiza os loggers sem reiniciar a coleta; se estiver parada, as opções valem ao próximo início.
- Um intervalo já salvo acima de 60.000 ms é preservado enquanto o campo não for editado; após editar, a tela aceita somente 100–60.000 ms.
- Na carga, `Normalize()` corrige valores fora de faixa: `updateIntervalMs` ≥ 100, `overlayOpacity` 0.1–1.0, `overlay.width` 140–700, `overlay.fontSize` 8–20, `overlayPosition.x/y` em ±5000, `accentColor` inválido volta para `#1E90FF`, `telemetryExport.port` travada em 1024–65535, cooldowns de alerta em 1–3600 s (regra: 0–3600 s), perfis com os mesmos clamps de offset/largura/fonte/opacidade e `appMatch` limpo (sem `.exe`), `customSensors` com máx. 64 fórmulas (inválidas são descartadas com nota em `logs/errors.log`) e `overlay.graphs` com altura 12–120 px, janela 30–1200 amostras e máx. 12 gráficos.
