# AppConfig / Config

> **Idioma:** Português (Brasil) · [English](../en/functions/configuration.md)

Sistema de configuração baseado em JSON. O arquivo `config.json` fica na raiz do projeto e é carregado automaticamente.

## Modelos

### AppConfig
- `updateIntervalMs` - Intervalo de atualização em milissegundos (padrão: 1000)
- `logToCsv` - Habilitar logging em CSV
- `logToJson` - Habilitar logging em JSON
- `logToJsonJournal` - Journal JSON contínuo (JSONL): uma linha por amostra em `logs/sensors_*.jsonl` + notas nos marcadores de sessão (início/resets) — ver [logging.md](logging.md)
- `showOverlay` - Mostrar overlay na inicialização; no app único, também controla sua visibilidade pelo dashboard
- `toggleOverlayHotkey` - Atalho global para mostrar/ocultar o OSD (ex: "Ctrl+Shift+O"; vazio = desativado)
- `pauseMonitorHotkey` - 2º atalho global (P6.7): pausa/retoma o monitoramento (padrão `Ctrl+Alt+P`; vazio = desativado; estado de pausa não persiste) — ver [hotkey.md](hotkey.md) e [hardware-monitor.md](hardware-monitor.md)
- `theme` - Tema visual builtin do app unificado: `"neptune"` (visual original do Avalonia) ou `"slate"` (porta verbatim do visual WPF/Windows), aplicado no boot e trocável **em runtime** pelo seletor em Configurações → INTERFACE (Fase 3 — sem reiniciar); configs da era F1.6 com `uiTheme` migram para o novo campo na leitura (o arquivo passa a escrever só `theme`). Valor desconhecido volta a `neptune` e a CLI `--theme=<nome>` sobrepõe o arquivo (útil para comparar os dois temas sem editar o config; a grafia legada `--ui-theme=` segue aceita) — ver [interface-principal.md](interface-principal.md)
- `overlayOpacity` - Opacidade do overlay (0-1)
- `overlayPosition` - Posição inicial do overlay (X, Y)
- `Overlay` - Configurações específicas do overlay
- `Sensors` - Quais sensores estão habilitados
- `alerts` - Regras de alerta com ações (popup/som/script/desligar); a página do app único edita limites, cooldown e notificação interna — ver [alertas.md](alertas.md)
- `telemetryExport` - Exportador HTTP do SDK de telemetria: `{ enabled: false, port: 7317, listenAll: false }` — publica o último snapshot como JSON em `GET /values` (mesmo payload do `sensors_latest.json`) e `GET /health`; padrão é loopback (`listenAll: true` abre para a LAN e pode exigir firewall) — ver [telemetry-sdk.md](../telemetry-sdk.md)
- `update` - Verificação de atualizações via GitHub Releases: `{ autoCheck: true, checkBeta: false, repository: "" }` — checagem automática no arranque + timer de 24h (toggle em Configurações) e botão manual; `checkBeta` aceita releases marcadas como pré-release ("canal beta"); `repository` vazio usa o oficial (`BadTonho/PhantomMonitor`; também aceita URL do GitHub) — implementado em `PhantomMonitor.Services.Update` (Core) e compartilhado pelas plataformas
- `profiles` - Perfis por aplicativo do OSD: `[{ name, appMatch, offsetX?, offsetY?, width?, fontSize?, opacity?, accentColor?, series?, visible? }]` — ver [overlay.md](overlay.md); `appMatch` vazio ou `*` casa com qualquer executável; `visible: false` esconde o OSD enquanto o perfil casa (`opacity` vazio/nulo usa a `overlayOpacity` global)
- `overlay.templates` - Linhas personalizadas do OSD: `[{ id, text, color? }]` com placeholders `{Chave}`/`{Chave:N}`/wildcard; linha oculta até todos os placeholders resolverem; editáveis no seletor de conteúdo do bloco — ver [overlay.md](overlay.md)
- `customSensors` - Sensores customizados do usuário: `[{ name, formula, unit? }]` — fórmulas derivadas de outras séries (`{GpuTemperature} - {CpuTemperature}`, funções `min/max/abs`, wildcard), publicadas como série `Custom#i` a cada ciclo (entra em stats/CSV/SDK/alertas); editável pela página **Sensores customizados** do app único — ver [sensores-customizados.md](sensores-customizados.md)
- `sensorTuning` - Cadência de amostragem + suavização **por série** (P9.1): `[{ series, intervalMs?, smoothing? }]` — entre amostras a série repete o último valor publicado, as estatísticas pulam os pontos repetidos e a média móvel atravessa o jitter nos refreshes — ver [ajuste-de-series.md](ajuste-de-series.md)
- `overlay.graphs` - Sparklines por série no OSD: `[{ id, series, height?, window?, color?, min?, max? }]` — resolução igual às templates (chave/wildcard/nome), buffer circular por gráfico, eixo auto ou fixo; editáveis no seletor de conteúdo — ver [overlay.md](overlay.md) (P8.5)

### OverlayConfig
- `width` - Largura do overlay em pixels
- `showCpu/Gpu/Ram/Disk/Network/Fans/Motherboard/Fps/FrametimeGraph` - Opções legadas usadas ao migrar arquivos sem `blocks`; a visibilidade atual é definida pelos itens de `blocks`.
- `compactMode` - Modo compacto
- `accentColor` - Cor de destaque (hex, ex: "#1E90FF")
- `shownSeries` - Whitelist legada (padrões wildcard) usada ao migrar arquivos sem `blocks`; as séries atuais são definidas pelos itens `series` dos blocos.
  além das linhas canônicas: `"Gpu*"` (multi-GPU), `"FanSpeed*"` (ventoinhas extras),
  `"Disk*"` (R/W, temperatura e saúde por disco), `"CpuTemperature#*"`, `"CpuPerCore#*"`,
  `"CpuClock#*"` (telemetria por núcleo), `"VrmTemperature*"`, `"RailVoltage*"`,
  `"Network*"` (por interface + totais da sessão). `"*"` libera tudo. Padrão:
  `["Gpu*", "FanSpeed*", "Disk*"]`. Lista vazia/ausente no config volta ao padrão.
  Os padrões são conferidos por `SensorKey.MatchesPattern` (sem distinção de maiúsculas).
- `layout` - Posições normalizadas dos blocos (`cpu`, `gpu`, `ram`, `disk`, `network`, `fans`, `motherboard`, `fps`, `extras`), cada uma com `x` e `y` entre 0 e 1.
- `blocks` - Lista de blocos personalizados: `[{ id, x, y, items: [{ kind, value }] }]`. `x`/`y` são normalizados (0–1); `kind` aceita `category`, `series`, `graph`, `template` e `frametime`. Um bloco pode reunir vários itens. O campo `layout` e as opções legadas `show*` continuam sendo lidos na migração de configurações antigas; após salvar, `blocks` é a fonte do layout.

### SensorConfig
- `Cpu/Gpu/Ram/Disk/Network/Motherboard` - Sensores habilitados

## Carregamento e localização

O caminho é resolvido por `ConfigPaths.ConfigFile` (independente da pasta de saída do build):

1. **Desenvolvimento:** sobe a partir do executável até encontrar `PhantomMonitor.slnx` (ou `.sln`) e usa o `config.json` da raiz do repositório.
2. **Publicado/instalado:** usa `%AppData%\PhantomMonitor\config.json` no Windows e `$XDG_CONFIG_HOME/PhantomMonitor/config.json` no Linux (normalmente `~/.config/PhantomMonitor/config.json`).

Se o arquivo não existir, `AppConfig.Load` copia o `config.example.json` (da mesma pasta ou ao lado do executável — o `.csproj` o copia para a saída) e cria o diretório se necessário.

## Validação e persistência

- `AppConfig.Normalize()` corrige valores inválidos (intervalo mínimo de 100 ms, opacidade entre 0,1 e 1, `overlayPosition.x/y` em ±5000, objetos `null`).
- `AppConfig.Save()` grava de forma atômica (arquivo `.tmp` + substituição) e retorna `bool`; o app avisa (na página e em `logs/errors.log`) se a gravação falhar.
- Falhas de leitura/gravação são registradas em `logs/errors.log` (ver [logging.md](logging.md)).
- `showOverlay` define o estado inicial do toggle "Overlay".
- **Toggles persistentes:** os checkboxes da sidebar (`Overlay no Jogo`, `Exportar CSV`, `Exportar JSON`, `Journal JSON (jsonl)`) gravam o estado no `config.json` ao serem alternados — antes só mudavam o estado vivo entre sessões de monitoramento.
- No editor do overlay, clique direito em área vazia cria um bloco; botão esquerdo arrasta; duplo clique escolhe conteúdo; clique direito no bloco abre editar/remover. Conteúdo e posição ficam em rascunho até **Salvar e aplicar**. Categorias, séries, gráficos e templates podem ser combinados em um bloco.
- `overlay.width`, `overlayOpacity`, `overlay.fontSize` e `overlay.compactMode` são ajustes globais editáveis na página **Configurações → Overlay flutuante**. `overlay.accentColor` continua editável via `config.json`.
- `toggleOverlayHotkey` registra um atalho global (Win32 `RegisterHotKey`) que alterna o OSD de qualquer aplicativo — ver [hotkey.md](hotkey.md). Editável via `config.json` (a seção "ATALHO GLOBAL" era do Editor Visual do WPF, aposentado na Fase 2); o app unificado honra o valor no Windows (janela message-only).
- `sensors.*` está reservado no modelo, mas ainda não é consumido pela interface.
- No app único, a página **Configurações** edita `updateIntervalMs` (100–60.000 ms), `logToCsv`, `logToJson` e `logToJsonJournal`. O diretório de logs é exibido como somente leitura.
- No app único, o card **INTERFACE** da mesma página edita `theme` pelo seletor **Tema visual** (Neptune = visual original do Avalonia; Slate = porta verbatim do visual do WPF). A escolha entra em vigor em **Salvar e aplicar**: o dicionário `Themes/Tokens.*.axaml` troca em runtime sem reiniciar — as views reagem na hora pelos `DynamicResource` e pincéis resolvidos pela VM re-resolvem no evento de troca; persistida no config e relida no boot. A CLI `--theme=<nome>` sobrepõe só a leitura do arquivo (smoke/troubleshooting).
- No app único, a mesma página edita `telemetryExport.enabled`, `port` (1024–65.535) e `listenAll`; o estado e os endpoints ativos são exibidos. O listener funciona durante a coleta, permanece ativo na pausa e fecha ao parar. Acesso por `listenAll` não tem TLS nem autenticação.
- No app único, a página **Sensores customizados** edita nome, fórmula, unidade e ordem das entradas `customSensors`; salvar recria o serviço de sensores e reinicia as estatísticas da sessão, preservando coleta parada ou pausada.
- No app único, a página **Ajuste por série** edita `sensorTuning`: alvos exatos ou padrões, `intervalMs` e `smoothing`, com reordenação para controlar precedência (a última regra correspondente vence). Salvar aplica a lista ao recriar o serviço de coleta; coleta parada ou pausada preserva seu estado. Ver [ajuste-de-series.md](ajuste-de-series.md).
- No app único, **Configurações → Overlay flutuante** abre o editor de composição, com canvas de grade e prévia ao vivo. `overlay.blocks` guarda ids estáveis, conteúdo ordenado e posições normalizadas relativas à janela monitorada no Windows ou à área disponível no Linux. O editor usa botão direito para criar, arraste para mover, duplo clique para selecionar conteúdo e botão direito sobre um bloco para editar/remover. O botão da visão geral alterna `showOverlay` e persiste a mudança imediatamente. Ver [overlay.md](overlay.md) para as limitações de posicionamento e fullscreen em Wayland.
- A página **Configurações → Overlay flutuante** edita a aparência global: `overlay.width` (140–700 px), `overlayOpacity` (10–100%), `overlay.fontSize` (8–20 pt, passos de 0,5) e `overlay.compactMode`. Salvar aplica os ajustes sem reiniciar a coleta.
- Ao carregar config antigo sem `overlay.blocks`, `Normalize()` migra categorias habilitadas, séries de `overlay.shownSeries`, gráficos, templates e posições de `overlay.layout`. Sem posições anteriores, cria uma coluna vertical na ordem padrão. O layout migrado é gravado no próximo **Salvar e aplicar**. Configuração nova começa com os blocos padrão atuais.
- No app único, a página **Alertas** edita regras do bloco `alerts`, incluindo caminho de script e argumentos. Scripts `.sh` usam `/bin/sh`; outros arquivos precisam ser executáveis. Som e desligamento são preservados, mas não executados no Linux.
- **Aplicação no Linux:** salvar persiste com `AppConfig.Save()`. Alterar o intervalo recria o serviço de sensores, zera as estatísticas da sessão e preserva coleta pausada/parada. Alterar apenas os formatos de log atualiza os loggers sem reiniciar a coleta; opções do exportador HTTP são reabertas sem reiniciar a coleta e, se ela estiver parada, valem ao próximo início.
- Um intervalo já salvo acima de 60.000 ms é preservado enquanto o campo não for editado; após editar, a tela aceita somente 100–60.000 ms.
- Na carga, `Normalize()` corrige valores fora de faixa: `updateIntervalMs` ≥ 100, `overlayOpacity` 0.1–1.0, `overlay.width` 140–700, `overlay.fontSize` 8–20, `overlayPosition.x/y` em ±5000, `accentColor` inválido volta para `#1E90FF`, `telemetryExport.port` travada em 1024–65535, cooldowns de alerta em 1–3600 s (regra: 0–3600 s), perfis com os mesmos clamps de offset/largura/fonte/opacidade e `appMatch` limpo (sem `.exe`), `customSensors` com máx. 64 fórmulas (inválidas são descartadas com nota em `logs/errors.log`) e `overlay.graphs` com altura 12–120 px, janela 30–1200 amostras e máx. 12 gráficos.
