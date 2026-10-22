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
| `Slate` | Visual do WPF hoje | `src/PhantomMonitor/Resources/Styles.xaml` (dark neutro, accent azul `#2563EB`) |
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
- [ ] Controles compartilhados em Avalonia: nav com estado selecionado, cards,
      inputs, banners (incluindo banner de atualização), gradiente/acento.
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
- [ ] Funcionalidades Windows-specific a conferir no port:
      elevação admin, PresentMon (processo externo — já orquestrado pelo Core),
      hotkey global, tray, startup com Windows, DebugWindow (manter ou não).
- [ ] Biblioteca nativa de sensores (`PhantomSensorLib`) via P/Invoke entregando
      igual no binário Avalonia do Windows.
- [ ] Unificar gráficos: `Controls/Minigraph.cs` (WPF) e `Controls/HistoryChart.cs`
      (Avalonia) viram **um** controle com cores por token.
- [ ] Portar o visual do WPF como `Themes/Tokens.Slate.axaml` (2º tema de fábrica).
- [ ] Build de release no Windows publicando o binário Avalonia — teste manual
      completo (acesso admin, fps, overlay, tray, update) e revisão dos docs.

**Critérios de aceite:** paridade funcional 1:1 com o WPF atual no Windows;
build 0/0; os dois temas builtin renderizam as mesmas views.

## Fase 2 — Aposentar o WPF

- [ ] `scripts/release.ps1` publica apenas o Avalonia (win-x64 + linux-x64).
- [ ] `packaging/windows/phantom-monitor.iss` aponta para o executável final.
- [ ] **Decisão de continuidade do updater:** manter os assets com a convenção
      atual (`PhantomMonitor-<v>-win-x64.zip` / `-linux-x64.tar.gz` / `.AppImage`) —
      nenhuma mudança no updater. Definir o AssemblyName final
      (recomendado: unificar no nome `PhantomMonitor.exe` para manter atalho do
      setup, Start Menu e relaunch pós-update) + como a troca portátil lida com
      executável antigo órfão (`.bak`) ou instruir que a release de transição
      aceite reinstalação completa no Windows.
- [ ] Remover `src/PhantomMonitor` (commit `chore:` separado, com `Styles.xaml`,
      janelas e `Controls/Minigraph.cs` aposentados juntos).
- [ ] Docs sincronizados: READMEs (pt/en), `interface-principal` /
      `main-interface`, `atualizações` / `updates`, packaging; linha do README
      raiz que diz "WPF continua sendo a aplicação principal" passa a descrever
      app única.

**Critérios de aceite:** Windows roda o app único; pipeline de release/update
intacto; WPF fora do repo; nenhum usuário órfão de atalhos quebrados.

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
