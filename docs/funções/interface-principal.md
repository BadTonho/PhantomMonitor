# Interface principal (app único)

> **Idioma:** Português (Brasil) · [English](../en/functions/main-interface.md)

Descrição da interface principal do app único do Phantom Monitor (Avalonia multiplataforma, Windows e Linux), com tema escuro e layout de sidebar + conteúdo. As diferenças operacionais no Linux (sensores nativos do kernel, D-Bus, Wayland, camada Vulkan) estão em [suporte-linux.md](suporte-linux.md); inventário, sensores customizados, ajuste por série, alertas e configurações são as mesmas páginas nos dois sistemas.

## Funcionalidades

- **Sidebar:** estado da execução (`Monitorando` / `Parado` / `Pausado` (P6.7)), tempo ativo, contagem de sensores, navegação entre as páginas (visão geral, inventário, sensores customizados, ajuste por série, alertas, configurações e diagnóstico), toggle `Overlay no Jogo`, controles de exportação (CSV/JSON/Journal JSONL — P8.3) e opção `Iniciar com o Windows` (registro `Run`, aplicada na hora).
- **Bandeja do Sistema:** Ícone com menu de contexto (Mostrar janela, Overlay no Jogo, Iniciar com o Windows, Resetar estatísticas, Pausar monitoramento, seção de perfis do OSD e Sair) — portada da bandeja do WPF na F1.5; ver [tray.md](tray.md).
- **Cards de Métricas Clean:** Cards dedicados para Processador (CPU), Placa de Vídeo (GPU), Taxa de Quadros (FPS), Memória RAM, Armazenamento (Disco), Rede (↓/↑) e Placa-mãe & Ventoinhas (maior temperatura + ventoinhas GPU/Sistema).
- **Linhas de Detalhes:** Os cards de CPU (clock, potência, voltagem) e GPU (VRAM usada/total, clock, potência, hotspot e temp de VRAM — P8.6) exibem uma linha secundária com os valores avançados disponíveis; quando nenhum sensor existe, informam a indisponibilidade.
- **Indicadores Visuais Sutis:** Barras de progresso horizontais finas (5px) e badges em pílula com borda translúcida para temperatura e frametime (sem efeitos neon ou círculos exagerados).
- **Lista de Processos Ativos:** Visualização limpa em tabela de processos consumindo a GPU.
- **Diagnóstico:** view no próprio app com os sensores detectados em tempo real e os erros recentes (port do `DebugWindow`) — ver [diagnostico.md](diagnostico.md).
- **Aviso de Elevação:** Alerta discreto quando a aplicação não possui privilégios administrativos.

## Auto-inicialização

O monitoramento inicia automaticamente ao abrir o aplicativo (com fallback seguro e suporte a controle manual via "Iniciar Monitor" e "Parar"). A pausa (P6.7) é só de runtime — via bandeja ou atalho global — e reiniciar o app retoma a coleta.

## Cores e Tipografia

- **Paleta Base:** Neutros escuros (`#0F1117` para fundo, `#141720` para sidebar, `#181B24` para cards com borda 1px `#232734`).
- **Tipografia Principal:** Textos em `#F1F5F9` (alto contraste sem saturação agressiva), rótulos em `#94A3B8` e legendas em `#64748B`.
- **Acentos Funcionais:** Azul royal (`#2563EB` / `#3B82F6`) para ações principais e barras de progresso.
- **Faixas de Temperatura e Uso Dinâmico:**
  - Uso < 75%: Branco/Slate neutro (`#F1F5F9`) para uso normal.
  - Uso 75% a 90%: Âmbar suave (`#F59E0B`).
  - Uso ≥ 90%: Coral (`#EF4444`).
  - Temperatura: Pílulas discretas com tons esmeralda (≤ 65°C), âmbar (≤ 80°C) ou coral (> 80°C).
- **Tokens de tema:** no app único, as cores e os acentos das
  views moram em dicionários de tema (`src/PhantomMonitor.App/Themes/Tokens.*.axaml`,
  46 chaves espelhadas entre os dois temas) e são consumidas via `{DynamicResource}`
  (as faixas dinâmicas usam brushes resolvidos por chave no ViewModel — mesmo
  catálogo). A F1.6 fecha os dois temas builtin: `Neptune` preserva o visual
  original do Avalonia e `Slate` porta verbatim o visual do WPF aposentado
  (`Styles.xaml`, banners/pílulas do shell `MainWindow.xaml` e painel do OSD
  `#CC070A0F`/`#25FFFFFF` do WPF — arquivos removidos na Fase 2 da unificação,
  proveniência por linha no dicionário). A escolha fica no
  `config.json` — `"theme": "neptune" | "slate"` (configs F1.6 com `uiTheme`
  migram na leitura), aplicada no boot e trocável **em runtime** pelo seletor
  em Configurações → INTERFACE (Fase 3: troca de dicionário sem reiniciar; a
  CLI `--theme=<nome>` sobrepõe a leitura do arquivo para comparação/smoke,
  com `--ui-theme=` legado aceito) — ver o
  [roadmap de unificação visual](../roadmap-unificacao-visual.md). Também na
  F1.6: nav da sidebar com estado selecionado (fundo + indicador 3×14) via
  tokens `Nav*`, toasts de alerta com tokens `AlertToast*` (borda/título âmbar
  no Slate, como o popup do WPF) e publicação win-x64 do binário unificado.
  Os botões de ação primários não têm token: seguem o accent do tema da
  plataforma (Fluent — no Windows, o acento do próprio sistema) nos dois temas.

## Elevação de privilégio

Se o aplicativo não estiver rodando como Administrador, exibe um banner com botão para reiniciar com privilégios elevados (necessário para a leitura irrestrita da CPU e rastreamento via PresentMon).
