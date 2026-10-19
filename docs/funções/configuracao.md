# AppConfig / Config

Sistema de configuração baseado em JSON. O arquivo `config.json` fica na raiz do projeto e é carregado automaticamente.

## Modelos

### AppConfig
- `updateIntervalMs` - Intervalo de atualização em milissegundos (padrão: 1000)
- `logToCsv` - Habilitar logging em CSV
- `logToJson` - Habilitar logging em JSON
- `showOverlay` - Mostrar overlay na inicialização
- `toggleOverlayHotkey` - Atalho global para mostrar/ocultar o OSD (ex: "Ctrl+Shift+O"; vazio = desativado)
- `overlayOpacity` - Opacidade do overlay (0-1)
- `overlayPosition` - Posição inicial do overlay (X, Y)
- `Overlay` - Configurações específicas do overlay
- `Sensors` - Quais sensores estão habilitados
- `alerts` - Regras de alerta com ações (popup/som/script/desligar) — ver [alertas.md](alertas.md)
- `telemetryExport` - Exportador HTTP do SDK de telemetria: `{ enabled: false, port: 7317, listenAll: false }` — publica o último snapshot como JSON em `GET /values` (mesmo payload do `sensors_latest.json`) e `GET /health`; padrão é loopback (`listenAll: true` abre para a LAN e pode exigir firewall) — ver [telemetry-sdk.md](../telemetry-sdk.md)
- `profiles` - Perfis por aplicativo do OSD: `[{ name, appMatch, offsetX?, offsetY?, width?, fontSize?, accentColor?, series?, visible? }]` — ver [overlay.md](overlay.md); `appMatch` vazio ou `*` casa com qualquer executável; `visible: false` esconde o OSD enquanto o perfil casa
- `overlay.templates` - Linhas personalizadas do OSD: `[{ text, color? }]` com placeholders `{Chave}`/`{Chave:N}`/ wildcard; linha oculta até todos os placeholders resolverem; cor só via config — ver [overlay.md](overlay.md)

### OverlayConfig
- `width` - Largura do overlay em pixels
- `showCpu/Gpu/Ram/Disk/Network/Fans/Motherboard/Fps` - Visibilidade de cada seção
- `showGpuProcesses` - Mostrar processos da GPU no overlay
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
2. **Publicado/instalado:** usa `%AppData%\PhantomMonitor\config.json`.

Se o arquivo não existir, `AppConfig.Load` copia o `config.example.json` (da mesma pasta ou ao lado do executável — o `.csproj` o copia para a saída) e cria o diretório se necessário.

## Validação e persistência

- `AppConfig.Normalize()` corrige valores inválidos (intervalo mínimo de 100 ms, opacidade entre 0,1 e 1, `overlayPosition.x/y` em ±5000, objetos `null`).
- `AppConfig.Save()` grava de forma atômica (arquivo `.tmp` + substituição) e retorna `bool`; o Editor de Overlay avisa se não conseguiu salvar.
- Falhas de leitura/gravação são registradas em `logs/errors.log` (ver [logging.md](logging.md)).
- `showOverlay` define o estado inicial do toggle "Overlay".
- **Toggles persistentes:** os checkboxes da sidebar (`Overlay no Jogo`, `Exportar CSV`, `Exportar JSON`) gravam o estado no `config.json` ao serem alternados — antes só mudavam o estado vivo entre sessões de monitoramento.
- `overlay.showNetwork`, `overlay.showFans` e `overlay.showMotherboard` já são consumidos pelo overlay (linhas NET/FAN/MB, com auto-revelação no primeiro dado real) e editáveis pelo Editor Visual.
- `overlay.compactMode`, `overlay.accentColor` e `overlay.fontSize` são aplicados ao overlay (espaçamento reduzido, cor de destaque e tamanho base da fonte) e editáveis no Editor Visual.
- `toggleOverlayHotkey` registra um atalho global (Win32 `RegisterHotKey`) que alterna o OSD de qualquer aplicativo — ver [hotkey.md](hotkey.md). Editável na seção "ATALHO GLOBAL" do Editor Visual.
- `sensors.*` e `overlay.showGpuProcesses` estão reservados no modelo, mas ainda não são consumidos pela interface.
- Na carga, `Normalize()` corrige valores fora de faixa: `updateIntervalMs` ≥ 100, `overlayOpacity` 0.1–1.0, `overlay.width` 140–700, `overlay.fontSize` 8–20 e `accentColor` inválido volta para `#1E90FF`.
