# Fase 1 — Mapa de equivalência WPF → Avalonia

> **Idioma:** Português (Brasil) · [English](en/unification-phase1-map.md)

> Auditoria fechada em out/2026 sobre a leitura efetiva dos dois frontends.
> Fontes: `src/PhantomMonitor` (WPF: `MainWindow.xaml`/`.cs`, `Overlay/*`,
> `DebugWindow`, `InventoryWindow`, `Services/`) e `src/PhantomMonitor.App`
> (Avalonia: views, `ViewModels/*`, `Services/*`), além de `PhantomMonitor.Core`.
> Regra de decisão: **a UI que já existe no Avalonia é o formato alvo** (views na
> sidebar); o que o WPF tem a mais é portado; o que o Avalonia tem a mais fica.
> Tudo renderiza os dois temas builtin da Fase 0 (`Neptune`/`Slate`).

---

## 1. Decisões de formato (janela a janela)

| WPF | Formato alvo (Avalonia) | Estado hoje |
|---|---|---|
| `MainWindow` (shell) | `Views/MainWindow.axaml` (7 views na sidebar) | Paridade estrutural; ver §2 |
| `Overlay/OverlayWindow` | Janela própria (já existe) | Portar perfis P3, graph rows, clique-through (Windows) |
| `Overlay/OverlayEditorWindow` | Seção "OVERLAY FLUTUANTE" nas Configurações (formato já existe) | Portar perfis; posicionamento rápido/preview são nice-to-have |
| `Overlay/AlertPopupWindow` | Toast in-app (já existe) + notificação DBus no Linux (já existe) | Conviver por SO (toast no Windows, DBus no Linux) |
| `DebugWindow` | View Diagnóstico (já existe, ~paridade) | Absorver; extra: dump em texto copiável |
| `InventoryWindow` | View Inventário (parcial) | ✅ portado (F1.4): stats de sessão nas rows, árvore de hardware, monitores (EDID), reset com marker |

Lixo visual constatado no WPF: os `RadioButton` "SENSORES" (CPU/GPU/RAM/Disco/Rede)
da sidebar **não têm handlers** — não portar.

## 2. Dashboard (view Visão geral)

Forma alvo = layout Avalonia (cards com gráfico), absorvendo o que falta:

| Funcionalidade | WPF | Avalonia | Ação na Fase 1 |
|---|---|---|---|
| Cards CPU/GPU com gráfico | ❌ (barra+pílulas) | ✅ | manter formato Avalonia |
| Temperatura com pílulas dinâmicas (cor por faixa) | ✅ | parcial (texto) | ✅ feito (F1.2 — tints translúcidos + família quente) |
| Linhas de detalhe (CPU: clock/potência/volt; GPU: VRAM/clock/hotspot) | ✅ (`SensorFormatter`) | ❌ | ✅ feito (F1.2 — formatador do Core) |
| FPS + stats (mín/avg/1%/0.1%) | ✅ | ✅ | igual |
| Gráfico de frametime no dashboard | ❌ | ✅ | manter Avalonia |
| Card Disco (R/W) | ✅ | ✅ | igual |
| Card Rede (↓/↑) | ✅ | ✅ | igual |
| Card RAM (uso/total/disponível) | ✅ | ✅ | igual |
| Card Placa-mãe & ventoinhas | ✅ | ❌ | ✅ feito (F1.2) |
| **Lista de processos na GPU** | ✅ (`data.GpuProcesses`) | ❌ | ✅ feito (F1.2) |
| Badge "Nova versão" na sidebar | ✅ | (só toast) | ✅ feito (F1.2 — chip verde no ESTADO) |
| Banner de atualização | ✅ | (card de settings + toast) | manter formato Avalonia |
| Banner de elevação admin + restart como admin | ✅ | ❌ (Linux, por natureza) | ✅ feito (F1.2 — app único no Windows; UAC) |
| Toggles CSV/JSON/JSONL na sidebar | ✅ | em Configurações | manter formato Avalonia |
| `Minigraph` no dashboard | ❌ (só no overlay) | HistoryChart | Minigraph fica no overlay até unificar |

Tokens novos criados na F1.2 (os dois temas): família quente
`AccentHotBrush`/`AccentHotSoftBrush`/`AccentHotBodyBrush` (Slate verbatim
`#EF4444`/`#FCA5A5`/`#F87171` do WPF; Neptune específico) e tints translúcidos
de pílula/badge/banner `BgTint{Green,Warm,Hot}Brush` +
`BorderTint{Green,Warm,Hot}Brush`. O verde do badge de update reusa
`BgToastUpdateBrush` + `AccentGreenSoftBrush` já existentes.

## 3. Janelas e serviços — o que porta em detalhe

### Overlay (`Overlay/OverlayWindow`, 1227 ln de code-behind no WPF)

| Recurso | WPF | Avalonia | Ação |
|---|---|---|---|
| Arrastar pelo header / Botão ocultar | ✅/(comportamento próprio) | ✅/✅ | igual no Linux; **no Windows o painel é clique-through (como o WPF): não arrasta e o × não é clicável** — controle pelo dashboard (Ocultar overlay) e offsets do config/perfis |
| Modo compacto | ✅ | ✅ | igual |
| Whitelist por série/instância | ✅ | ✅ | igual (+ whitelist por perfil, perfil > global) |
| **Perfis por aplicativo (P3)** + pin via bandeja | ✅ (`RefreshProfileFor`/`ApplyProfile`) | ✅ F1.3 (matcher `AppMatch` + overrides; sem pin) | ✅ portado (`MainWindowViewModel.OverlayProfiles.cs`); pin pela bandeja fica na F1.5 |
| **Graph rows por série** (Minigraph com guides/min/max) | ✅ | ✅ F1.3 (HistoryChart + `MinY`/`MaxY`/`MaxFloor`/`GuideAt`/`GuideBrush`) | ✅ portado com o HistoryChart estendido |
| **Templates do OSD (P6.6)** | ✅ | ✅ F1.3 (gap não mapeado na tabela original) | ✅ portado — parser extraído para o Core (`OverlayPlaceholderParser`), mesmo resolver dos gráficos |
| Clique-through real (`WS_EX_TRANSPARENT`) | ✅ | ✅ F1.3 (Windows via P/Invoke; Linux mantém arrasto/clique) | ✅ portado (`OverlayWindow.Windows.cs`) |
| Topmost timer / posição pelo app em foco | ✅ | ✅ F1.3 (timer 500 ms + follow 30 ms, skip self/explorer, DPI) | ✅ portado |
| Cor de acento por perfil | ✅ | ✅ F1.3 (`OverlayAccentBrush` + tint 0x30 do painel) | ✅ portado |

Pendências explícitas da F1.3: **pin do perfil pela bandeja** (F1.5, com o
`TrayIconService`) e **posicionamento rápido/preview** (o `OverlayEditorWindow`
do WPF pesa 596+354 ln; sem a posição pelo app em foco já resolvida, o
editor de oferta é reavaliado junto da subfase de sistema).

### Serviços de sistema

| Serviço | WPF (onde) | Avalonia | Ação |
|---|---|---|---|
| Bandeja + menu (overlay/startup/reset/pausa/pin de perfil) | `TrayIconService` (369 ln) | ✅ `TrayIconService` (App) | ✅ F1.5: `TrayIcon` do Avalonia + menu nativo reconstruído; clique esquerdo ativa; StatusNotifier no Linux |
| Hotkeys globais (overlay + pausa, P6.7) | `HotkeyService` (96 ln) + `HotkeyParser` (Core) | ✅ Windows | ✅ F1.5 no Windows (`GlobalHotkeyService` no Core, janela message-only); Linux: X11/XWayland adiado (Wayland puro não permite por design) |
| Iniciar com o Windows (registro `Run`) | `StartupManager` (56 ln) | ✅ `StartupManagerFactory` | ✅ F1.5: `Run` do HKCU no Windows + XDG autostart no Linux |
| Instância única | `App.xaml.cs` (mutex) | ✅ `SingleInstanceGuard` | ✅ F1.5: mutex `Local\PhantomMonitor.SingleInstance` (Windows) + lock `logs/session.lock` (Linux) |
| Elevação admin | inline (`IsRunningAsAdmin`/`runas`) | ✅ (desde a F1.2) | nada a fazer — banner + "Executar como admin" já portados |
| Alertas: avaliador | `AlertRuleEvaluator` **Core** ✅ | ✅ | igual |
| Alertas: ações | popup + `MessageBeep` + script + shutdown (com guard `allowShutdown`) | ✅ por SO (`IAlertActions`) | ✅ F1.5: orquestração extraída ao Core — Windows com paridade integral (beep/script/shutdown+guard), Linux com script + notificação (som/shutdown não executados) |
| UI de regras de alerta | ❌ (config.json) | ✅ | manter Avalonia |
| Atualizações | ✅ (Core + Windows applier) | ✅ (Core + applier por SO) | ✅ F1.5: applier por SO no `InitUpdateService` (Windows zip / Linux tarball) |
| UI de custom sensors / ajuste por série / exportador HTTP | ❌ | ✅ | manter Avalonia |

### Leitura de sensores — o achado crítico (F1.1 ✅ concluída)

- **F1.1 feita:** `HardwareMonitorService` (1124 ln), `FpsMonitor`
  (495 ln — PresentMon) e `Win32Interop` migraram para
  `Core/Services/Windows/`; `InventoryBuilder` (92 ln, multiplataforma) para
  `Core/Services/`. A fábrica multiplataforma `SensorServiceFactory`
  consolidou no Core e substituiu a `LinuxSensorServiceFactory` — os dois
  frontends sobem o provedor correto por SO (`SensorServiceFactory.Create(updateIntervalMs, config)`),
  com guard `OperatingSystem.IsWindows()` + `[SupportedOSPlatform("windows")]`
  e pacotes Windows (LibreHardwareMonitor, System.Management, PerformanceCounter,
  EventLog) no Core. `PresentMon.exe` passou a acompanhar o binário do App;
  smoke validou Avalonia e WPF lendo sensores Windows pelo mesmo código Core.
- `PhantomSensorLib` não é consumida por .NET (zero `DllImport` no repo) — é
  usada pela camada Vulkan (C++). Não afeta a porta.

## 4. Ordem sugerida (subfases da Fase 1)

1. **F1.1 — Serviços Windows → Core** (+ fábrica multiplataforma; o Avalonia
   passa a monitorar no Windows). Código puro, sem UI — mata o risco crítico cedo.
   **✅ Feito (ver "Leitura de sensores" acima).**
2. **F1.2 — Dashboard completo no Avalonia** (linhas de detalhe, processos GPU,
   MB&ventoinhas, badge/banners) com os dois temas renderizando.
   **✅ Feito (ver tabela acima; a partial `MainWindowViewModel.Overview.cs`
   concentra os extras).**
3. **F1.3 — Overlay completo** (perfis P3 + pin, graph rows, clique-through no
   Windows) e configurações com o que falta (posicionamento rápido/preview).
   **✅ Feito (runtime integral; `91578b4`):** perfis por app (matcher +
   overrides com base no config; pin espiando via `_pinnedProfileName`),
   graph rows com eixo fixo (HistoryChart estendido), templates P6.6 +
   gráficos via `OverlayPlaceholderParser` na Core, clique-through/follow/
   topmost no Windows (`OverlayWindow.Windows.cs`), acento por perfil com
   token de guia novo (`OverlayGuideBrush`). **Adiado:** pin pela bandeja
   (F1.5) e posicionamento rápido/preview (reavaliar na subfase de sistema).
4. **F1.4 — Inventário completo** (stats de sessão, árvore de hardware,
   monitores/EDID, reset com marker no CSV/journal).
   **✅ Feito (`2a479ef`):** colunas Mín/Máx/Média da sessão nas rows dos
   grupos (mesmo formatador do valor), card de árvore `[HWR]` com
   assinatura anti-colapso, card de monitores reconciliado por identidade
   e botão "Resetar estatísticas" com `WriteMarker`/`WriteNote` — a mesma
   fonte Core (`InventoryBuilder`/`SeriesStats`/`MonitorInfo`) que o WPF
   consumia, apresentada em cards da página Inventário.
5. **F1.5 — Sistema** (tray, hotkeys, startup, instância única, admin) e
   alertas (orquestração de ações por SO).
   **✅ Feito (`9f49ba0`):** bandeja no app unificado (`TrayIconService` — menu do
   WPF + pin de perfil via `SetPinnedProfile`, clique esquerdo), hotkeys no
   Windows (`GlobalHotkeyService` — janela message-only em thread dedicada),
   startup por SO (`StartupManagerFactory`: registro Run + XDG autostart) com
   checkbox "Iniciar com o sistema", instância única (`SingleInstanceGuard`),
   alertas por SO (`IAlertActions`/`AlertActionFactory`) e applier de update por
   SO. Admin sem gap desde a F1.2. Linux: hotkeys adiado (X11/XWayland) e
   bandeja sujeita ao suporte a StatusNotifier do desktop.
6. **F1.6 — Visual WPF portado como tema "Slate"** integral + build/release no
   Windows do binário Avalonia (critérios de aceite da Fase 1 no roadmap).

## 5. Tamanho da porta (referência)

| Arquivo WPF | Linhas | Destino |
|---|---|---|
| `Overlay/OverlayWindow.xaml.cs` | 1227 | Overlay Avalonia (perfis/graf/clique-through) |
| `Services/HardwareMonitorService.cs` | 1124 | Core/Services/Windows |
| `Overlay/OverlayEditorWindow.xaml.cs` | 596 | apply de config (view Configurações) |
| `Services/FpsMonitor.cs` (PresentMon) | 495 | Core/Services/Windows |
| `Services/TrayIconService.cs` | 369 | tray do Avalonia (multi-SO) |
| `Overlay/OverlayEditorWindow.xaml` | 354 | referência visual da view Configurações |
| `MainWindow.xaml.cs` | 739 | ViewModel do Avalonia (lógica já parcialmente lá) |
| `DebugWindow`/`InventoryWindow`/`AlertPopup` | ~470 | views Diagnóstico/Inventário/toast |
| `Services/HotkeyService.cs` | 96 | hotkeys do Avalonia |
