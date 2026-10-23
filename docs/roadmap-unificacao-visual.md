# Roadmap — Unificação visual (frontend único Avalonia + sistema de temas)

> **Idioma:** Português (Brasil) · [English](en/visual-unification-roadmap.md)

> Plano aprovado em out/2026 — **Fase 0 concluída** (`0a6ee28` = plano,
> `0a00bf6` = tokens Neptune/Slate); Fases 1–3 pendentes.
> Origem: comparativo entre os frontends WPF (Windows) e Avalonia (Linux). Os visuais
> divergem desde a criação e cada mudança de interface custa o dobro. Decisão do
> mantenedor: adotar o **Caminho 2** (app única), preservando os dois visuais atuais
> como temas builtin e adiantando a fundação do futuro sistema de temas.

---

## Objetivo

1. **Unificar o frontend:** aposentar o WPF e manter o Avalonia como app única
   multiplataforma (`net10.0`, win-x64 + linux-x64). O `PhantomMonitor.Core` e os
   serviços continuam compartilhados como hoje.
2. **Salvar os dois visuais que existem hoje:** cada aparência vira um **tema
   builtin** baseado em tokens — nada do que existe de visual atual é perdido.
3. **Adiantar o sistema de temas futuro:** tokens trocáveis em runtime (sem
   rebuild de views), persistidos no `config.json`.

## Por que em fases nessa ordem

A **tokenização do Avalonia vem antes da migração** (Fase 0 antes da Fase 1):
a migração já nasce "temável" (o visual do WPF é portado como segundo tema de
fábrica) e a UI não é movida duas vezes.

## Decisões de arquitetura (os alicerces)

1. **Tokens, nunca hex inline:** cores, tipografia, raios e paddings vivem em
   `Themes/Tokens.<Tema>.axaml` (~20–25 chaves por tema). Nenhuma view enxerga
   valores absolutos.
2. **Controles compartilhados:** um único conjunto de estilos de nav/cards/
   botões/inputs/banners. O tema troca **só os tokens** — jamais duplicar UI
   por tema.
3. **`{DynamicResource}`** (não `StaticResource`) em toda cor/valor temável:
   pré-requisito para trocar tema em runtime com a UI viva.
4. **`ThemeVariant` nativo** do Avalonia (Light/Dark) por tema — hoje já em uso
   (`RequestedThemeVariant="Dark"`).
5. **Escopo mínimo inicial:** dois temas builtin (abaixo) + troca manual via
   dicionário. Fora de escopo inicial: Light mode, temas custom via JSON,
   import/export, live preview.

### Nomes de trabalho dos temas

| Tema de trabalho | Origem | Referência |
|---|---|---|
| `Slate` | Visual do WPF (aposentado na Fase 2) | `src/PhantomMonitor/Resources/Styles.xaml` (removido com o WPF; tokens verbatim em `src/PhantomMonitor.App/Themes/Tokens.Slate.axaml`) — dark neutro, accent azul `#2563EB` |
| `Neptune` | Visual do Avalonia hoje | `src/PhantomMonitor.App/Views/MainWindow.axaml` (cinza-azulado) |

> Nomes fechados na Fase 0 (`0a00bf6`): `Slate` = visual do WPF; `Neptune` = visual do Avalonia.

## O que NÃO sobrevive 1:1 (expectativas honestas)

- **Fonte exata:** Segoe UI não existe no Linux; o tema define um stack de
  fallback. Renderização de fonte é do sistema da máquina — fica "igual", não
  pixel-perfect.
- **Chrome de janela:** Mica/acrylic/titlebar nativa do Windows não existe no
  Linux; o Avalonia desenha o titlebar custom que aproxima, mas é migração,
  não tema.
- Métrica essencial: **paleta/tipografia/estilo preservados; plataforma escolhe
  a renderização.**

---

## Fase 0 — Tokenização do Avalonia atual — ✅ CONCLUÍDA (`0a6ee28`, `0a00bf6`)

- [x] Criar `src/PhantomMonitor.App/Themes/Tokens.Neptune.axaml` com a paleta
      atual do Avalonia (todos os hex hoje inline) + métricas (raios, paddings,
      escala tipográfica, fonts stack).
- [x] Substituir hex hardcoded em `Views/MainWindow.axaml` e `Views/OverlayWindow.axaml`
      por `{DynamicResource …}` (chaves espelhadas ao dicionário do WPF).
- [x] `App.axaml` carrega o dicionário ativo — estrutura de merge pronta para a Fase 3.
- [x] Smoke: alternar manualmente entre dois dicionários editando `App.axaml` —
      UI inteira muda de tema sem quebrar nada.
- [x] Docs: nota de tokens em `docs/funções/interface-principal.md`
      (+ espelho `docs/en/functions/main-interface.md`).

**Como ficou (`0a00bf6`):** 30 tokens por dicionário (`Neptune` = visual do
Avalonia verbatim; `Slate` = paleta do WPF do `Styles.xaml`, com estimativas
na mesma família para os tokens sem equivalente). Métricas (raios/paddings/
tipografia) ficam para a Fase 1, junto com a unificação dos controles.
Detalhe técnico: `HistoryChart.Stroke` passou de `Color` para `IBrush?`
(propriedade de cor não consome brush de tema). Validação: build Release
0/0, cobertura estática completa (30 tokens usados, nenhum órfão) e smoke
de runtime nos dois temas (app viva por 10 s com kill limpo).

## Fase 1 — Mapeamento e porta das telas do WPF (o grosso)

Primeira tarefa, antes de tudo: **auditar equivalências** entre as janelas e os fluxos.

- [x] Mapa de equivalência WPF → Avalonia (janela vs. view da sidebar):
      `MainWindow`, `OverlayWindow`, `OverlayEditorWindow`, `AlertPopupWindow`,
      `InventoryWindow`, `DebugWindow` — decidir, janela a janela, o formato final.
      **✅ Feito — ver [unificacao-fase1-mapa.md](unificacao-fase1-mapa.md)
      (decisões por janela, gaps, serviços Windows → Core e subfases F1.1–F1.6).**
- [x] **F1.1 — Serviços de leitura Windows → Core**: `HardwareMonitorService`,
      `FpsMonitor` (PresentMon) e `Win32Interop` em `Core/Services/Windows/`;
      `InventoryBuilder` (multiplataforma) direto em `Core/Services/`; fábrica
      multiplataforma única `SensorServiceFactory` no Core (o Avalonia deixa de
      subir `LinuxSensorServiceFactory` direto); pacotes Windows no Core
      (LibreHardwareMonitor, System.Management, PerformanceCounter, EventLog)
      sob `[SupportedOSPlatform("windows")]`; PresentMon agora acompanha o
      binário do App. Smoke: Avalonia e WPF coletando sensores Windows pelo
      mesmo código Core.
- [x] Controles compartilhados em Avalonia: nav com estado selecionado (indicador
      3×14 + fundo/token selecionado via tokens `Nav*`, F1.6), cards, inputs e
      banners tokenizados (toast de alerta ganhou `AlertToast*` na F1.6,
      incluindo o banner de atualização já por tokens). O "gradiente/acento" do
      WPF era recurso morto (definido no Styles.xaml, nunca consumido) e não
      rendeu port; os CTAs (`Button.action`) seguem o accent da plataforma
      (Fluent, dinâmico no Windows) nos dois temas, decisão de preservação do
      Neptune.
- [x] **F1.2 — Dashboard completo no Avalonia**: pílulas de temperatura com cor
      por faixa (verde/morno/quente/desconhecido) e métricas de uso pintadas
      (<75 primário, <90 morno, ≥90 quente — paridade do WPF); linhas de detalhe
      mono de CPU/GPU via `SensorFormatter` do Core; card **Placa-mãe &
      ventoinhas** (temp colorida + `BuildFanSummary`); card **Processos ativos
      na GPU** (WMI GPUEngine; estado vazio no Linux); badge de update no box
      ESTADO da sidebar (clique → Configurações); banner de elevação admin com
      "Executar como admin" (UAC) no Windows. Tokens novos nos dois temas:
      família quente (`AccentHotBrush`/`AccentHotSoftBrush`/`AccentHotBodyBrush`)
      e tints translúcidos (`BgTintGreen/Warm/HotBrush` +
      `BorderTintGreen/Warm/HotBrush`, reutilizados por badge e banner).
- [x] **F1.3 — Overlay completo no Avalonia**: perfis por aplicativo (P3 —
      matcher `AppMatch` do processo em foco com pin espiando via
      `_pinnedProfileName`; overrides de offsets/largura/fonte/opacidade/
      whitelist/acento/visibilidade com base no config; tag "◉ perfil: X"),
      graph rows por série com eixo fixo (HistoryChart estendido com
      `MinY`/`MaxY`/`MaxFloor`/`GuideAt`/`GuideBrush`) e templates P6.6 —
      parser extraído ao Core (`OverlayPlaceholderParser`); clique-through
      real (`WS_EX_TRANSPARENT`), topmost (500 ms) e posição pelo app em
      foco (30 ms) no Windows; acento do OSD como dado do config
      (`OverlayBrushHelper`) com token novo `OverlayGuideBrush`. No Windows
      o painel é clique-through como no WPF (sem arrasto/× clicável); no
      Linux continua arrastável. **Pendente:** pin pela bandeja (F1.5) e
      posicionamento rápido/preview (reavaliar na subfase de sistema).
- [x] **F1.4 — Inventário completo no Avalonia**: estatísticas de sessão
      (Mín/Máx/Média) nas rows dos grupos, alimentadas por `data.SeriesStats`
      com o formatador do valor (conversões de magnitude por estatística;
      NaN = "--"); card **Hardware detectado** com árvore `[HWR]`
      (`InventoryBuilder.BuildHardware`, reconstrução só quando a
      assinatura dos nós muda — anti-colapso como no WPF); card **Monitores
      (EDID)** com instância WMI/fabricante/modelo/código/serial/diagonal/
      fabricação e indicador PRIMÁRIO (coleção troca só quando o conjunto
      muda); botão **Resetar estatísticas** com `ResetSeriesStats` +
      `CsvLogger.WriteMarker` + `JsonJournalLogger.WriteNote` e confirmação
      transitória; resumo com "N nós · M monitor(es)" quando existirem.
- [x] **F1.5 — Sistema**: bandeja no app unificado (`TrayIconService` —
      mesmos itens/checks do WPF, clique esquerdo, ícone desenhado em runtime;
      StatusNotifier no Linux) com pin de perfil pela bandeja (`SetPinnedProfile`,
      fecha o pendente da F1.3); hotkeys globais no Windows
      (`GlobalHotkeyService` no Core, janela message-only em thread dedicada,
      IDs `0x5048`/`0x5049` — Linux adiado: X11/XWayland no futuro, Wayland puro
      não permite por design); startup com o sistema por SO
      (`StartupManagerFactory` — registro Run no Windows, XDG autostart no
      Linux) com checkbox "Iniciar com o sistema"; instância única
      (`SingleInstanceGuard` — mutex `Local\PhantomMonitor.SingleInstance` no
      Windows, lock de arquivo no Linux); alertas por SO extraídos ao Core
      (`IAlertActions`/`AlertActionFactory` — Windows com beep/script/shutdown
      + guard `allowShutdown` na paridade do WPF, Linux com script + notificação
      de desktop); applier de update por SO (Windows zip / Linux tarball). Elevação
      admin sem gap desde a F1.2. (`9f49ba0`)
- [ ] Funcionalidades Windows-specific a conferir no port:
      elevação admin ✅ (F1.2/F1.5), PresentMon (processo externo — já orquestrado pelo Core),
      hotkey global ✅ (F1.5, Windows), tray ✅ (F1.5), startup ✅ (F1.5),
      DebugWindow (manter ou não).
- [ ] Biblioteca nativa de sensores (`PhantomSensorLib`) via P/Invoke entregando
      igual no binário Avalonia do Windows.
- [x] Unificar gráficos — **resolvido com a aposentadoria do WPF (Fase 2)**: o
      WPF nunca consumiu o Minigraph no dashboard; o controle foi removido
      junto ao frontend (`6c4d2c0`) e o `HistoryChart` (cores por token) é o
      único gráfico do app.
- [x] **F1.6 — Visual WPF portado como tema "Slate" integral + build/release no
      Windows** (`ae01a89`): `Tokens.Slate.axaml` verbatim do WPF (paleta do
      `Styles.xaml`, banners/pílulas do `MainWindow.xaml`, painel do OSD
      `#CC070A0F`/`#25FFFFFF`, toast do `AlertPopupWindow` — proveniência por
      linha no próprio dicionário), tokens `Nav*` + estado selecionado da nav com
      indicador 3×14 (port do RadioButton do WPF), tokens `AlertToast*` no toast
      de alerta, `uiTheme` (neptune|slate) em `config.json` aplicado no boot —
      CLI `--ui-theme=<nome>` sobrepõe para smoke; troca em runtime pela UI
      chega na Fase 3 — e publicação win-x64 self-contained single-file validada
      (`releases/win-x64/` com PresentMon.exe + config.example.json; build 0/0 e
      smoke vivo nos dois temas sem `errors.log`). PhantomSensorLib P/Invoke
      segue item próprio abaixo; cobertura revalidada (46 chaves homônimas nos
      dois dicionários, zero órfãs).

**Critérios de aceite:** paridade funcional 1:1 com o WPF atual no Windows;
build 0/0; os dois temas builtin renderizam as mesmas views.

## Fase 2 — Aposentar o WPF

- [x] **Fase 2 — O WPF foi aposentado do pipeline e do repo** (`a6f3e7f` feat +
      `6c4d2c0` chore):
      `scripts/release.ps1` publica apenas o app único (win-x64 + linux-x64) —
      payload do Windows capado em `PhantomMonitor.exe` + `PresentMon.exe` +
      `config.example.json` (o bundler single-file embute o PresentMon como
      nativo e espalha `.pdb`; o script garante a cópia solta e tira os pdbs),
      tarball com o binário `PhantomMonitor` + `config.example.json`; ISCC
      ausente tratado como aviso sem abortar (pipeline validado de ponta a
      ponta com `-Version 0.2.0-dev`: zip 44,1 MB, tarball 40,7 MB).
- [x] `packaging/windows/phantom-monitor.iss` aponta para o executável final
      **sem edição**: com o `AssemblyName` unificado, o exe do payload nasce
      exatamente com o nome `{app}\PhantomMonitor.exe` esperado pelo Source
      do stage.
- [x] **Decisão de continuidade do updater:** assets mantidos com a convenção
      atual (`PhantomMonitor-<v>-win-x64.zip` / `-linux-x64.tar.gz` /
      `.AppImage`) — nenhuma mudança no updater. `AssemblyName` final =
      **`PhantomMonitor`** (recomendação acatada: atalho do setup, Start Menu,
      Run key e relaunch pós-update permanecem válidos; e o
      `--exclude PhantomMonitor.exe` do PresentMon volta a casar com o nome
      real do app no Windows). A troca portátil não gera órfão: no Windows o
      nome é idêntico (antigo vai a `.bak`, o novo assume o lugar); no Linux o
      tarball da transição carrega um hardlink `PhantomMonitor.App` apontando
      para o binário novo, de custo zero no tar, para o relaunch por nome
      legado das instalações antigas abrir o app novo (shim aposentável nas
      próximas releases). URIs `avares://PhantomMonitor/...` atualizadas
      junto ao rename.
- [x] Remover `src/PhantomMonitor` (commit `chore:` separado: `6c4d2c0`, com
      `Styles.xaml`, janelas e `Controls/Minigraph.cs` aposentados juntos —
      24 arquivos; build do slnx 0/0). O item "unificar os gráficos" da Fase 1
      se resolve aqui: sem WPF, o `HistoryChart` é o único controle de gráfico.
- [x] Docs sincronizados (commit `docs:` seguinte): READMEs pt/en,
      `interface-principal` / `main-interface`, `atualizações` / `updates`,
      `configuracao` / `configuration` (Editor Visual aposentado), matrizes de
      paridade e `AGENTS.md`; a linha "WPF continua sendo a aplicação
      principal" passa a descrever o app.

**Critérios de aceite — atendidos:** Windows roda o app único; pipeline de
release/update intacto (validado com release local 0.2.0-dev, zip/tarball
conferidos por listagem); WPF fora do repo; nenhum usuário órfão de atalhos
quebrados (nome do executável preservado no Windows e shim de hardlink no
Linux). Smoke dos dois temas sem `errors.log`.

## Fase 3 — Sistema de temas visível ao usuário

- [ ] `config.json` + `Config.Normalize`: campo `theme: "neptune" | "slate"`
      (default a definir — discussão no commit).
- [ ] Seletor em **Configurações** (padrão dos outros settings) aplicando em
      runtime via troca de dicionário (`Application.Styles` dinâmico ou
      merge/unmerge de `ResourceDictionary`) — a UI reage na hora graças ao
      `DynamicResource` da Fase 0.
- [ ] Persistência e re-load no boot.
- [ ] (Futuro, fora de escopo agora) temas extras: Light mode, HDR, custom via
      arquivo externo, import/export.

**Critérios de aceite:** troca de tema sem reiniciar, sem rebuild de views,
persistida entre sessões; docs de configuração atualizadas.

---

## Riscos e cuidados

| Risco | Como a fase o trata |
|---|---|
| Perder funcionalidade Windows-specific no port | Mapa de equivalência é a **primeira** tarefa da Fase 1; paridade funcional 1:1 é critério de aceitação |
| Troca de tema não atualiza views vivas | `DynamicResource` desde a Fase 0 (por isso a ordem das fases) |
| Usuário portátil Windows com executável antigo/órfão após update | Decisão documentada na Fase 2 (nome final do executável + `.bak` + changelog da release de transição) |
| Drift visual voltar a acontecer | Regra do repo: **hex inline em `.axaml` é bug de revisão** |
| Duplicação de trabalho com a Rodada 5 | Ver seção abaixo |

## Relação com a Rodada 5 / roadmap-paridade

Os itens de polish da Rodada 5 que tocam XAML (painel do OSD, axis global,
polish do inventário) só compensam executar **uma vez** — na app unificada.
Ordem sugerida da fila de trabalho:

1. **Fase 0** (tokenização — barata).
2. Rodada 5 restante **só nos pontos que não dobram trabalho** (itens Linux
   como P1/P99 das percentis podem ir direto no Avalonia).
3. **Fases 1 → 2 → 3** desta unificação, absorvendo o restante da Rodada 5
   (painel/axis/polish) já dentro da porta.

## Inventário de referência (o que define cada visual hoje)

> Referência histórica: os arquivos `src/PhantomMonitor/**` abaixo foram
> removidos com o WPF na Fase 2 (`6c4d2c0`). A fonte de verdade atual do
> visual "Slate" é `src/PhantomMonitor.App/Themes/Tokens.Slate.axaml`.

| Arquivo | Papel |
|---|---|
| `src/PhantomMonitor/Resources/Styles.xaml` (254 ln) | Paleta nomeada + estilos do WPF — **fonte da verdade do visual "Slate"** |
| `src/PhantomMonitor/MainWindow.xaml` (477 ln) | Shell WPF |
| `src/PhantomMonitor/{Debug,Inventory,AlertPopup,OverlayEditor,Overlay}Window.xaml` | Janelas WPF a mapear na Fase 1 |
| `src/PhantomMonitor/Controls/Minigraph.cs` | Gráfico WPF (unificar com HistoryChart) |
| `src/PhantomMonitor.App/Views/MainWindow.axaml` (706 ln) | Shell Avalonia (consome `{DynamicResource}`) — fonte do visual "Neptune" agora é `Themes/Tokens.*.axaml` |
| `src/PhantomMonitor.App/Views/OverlayWindow.axaml` | Overlay Avalonia (consome `{DynamicResource}`) |
| `src/PhantomMonitor.App/Themes/Tokens.{Neptune,Slate}.axaml` | Dicionários de tema (30 tokens cada) — `0a00bf6` |
| `src/PhantomMonitor.App/App.axaml` (8 ln) | `FluentTheme` + `RequestedThemeVariant="Dark"` |
| `src/PhantomMonitor.App/Controls/HistoryChart.cs` | Gráfico Avalonia (unificar com Minigraph) |
| `src/PhantomMonitor.Core/Services/Update/` | Núcleo de atualização — **não muda** (detecção de release, banners e apply seguem como estão) |

## Obrigações de documentação (por fase)

- Fase 0: nota de tokens em `interface-principal` / `main-interface`.
- Fase 1: `interface-principal` refletindo app única; janelas mapeadas.
- Fase 2: READMEs (pt/en), `atualizações` / `updates` (se payload mudar no
  setup), packaging + build scripts em docs de configuração.
- Fase 3: `configuracao` / `configuration` com o campo `theme`.
