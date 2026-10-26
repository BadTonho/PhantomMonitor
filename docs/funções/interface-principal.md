# Interface principal (app único)

> **Idioma:** Português (Brasil) · [English](../en/functions/main-interface.md)

Descrição da interface principal do app único do Phantom Monitor (Avalonia multiplataforma, Windows e Linux), com tema escuro e layout de sidebar + conteúdo. As diferenças operacionais no Linux (sensores nativos do kernel, D-Bus, Wayland, camada Vulkan) estão em [suporte-linux.md](suporte-linux.md); inventário, sensores customizados, ajuste por série, alertas e configurações são as mesmas páginas nos dois sistemas.

Na implementação, a janela principal mantém a sidebar, a navegação e os elementos globais; Configurações, Sensores customizados, Alertas e Diagnóstico são controles de página próprios com o mesmo ViewModel compartilhado. Inventário, visão geral e ajuste por série continuam no XAML principal.

## Funcionalidades

- **Sidebar:** navegação com ícones, indicador da página selecionada, estado do monitoramento, badge de atualização e opção de iniciar com o sistema. A lista de navegação rola em janelas de altura reduzida.
- **Bandeja do Sistema:** Ícone com menu de contexto (Mostrar janela, Overlay no Jogo, Iniciar com o Windows, Resetar estatísticas, Pausar monitoramento, seção de perfis do OSD e Sair) — portada da bandeja do WPF na F1.5; ver [tray.md](tray.md).
- **Cards de Métricas Clean:** Cards dedicados para Processador (CPU), Placa de Vídeo (GPU), Taxa de Quadros (FPS), Memória RAM, Armazenamento (Disco), Rede (↓/↑) e Placa-mãe & Ventoinhas (maior temperatura + ventoinhas GPU/Sistema).
- **Linhas de Detalhes:** Os cards de CPU (clock, potência, voltagem) e GPU (VRAM usada/total, clock, potência, hotspot e temp de VRAM — P8.6) exibem uma linha secundária com os valores avançados disponíveis; quando nenhum sensor existe, informam a indisponibilidade.
- **Indicadores visuais:** gráficos de histórico e pílulas discretas de temperatura. Sem dados de FPS, o gráfico e as estatísticas ficam ocultos; voltam a aparecer quando há leitura. Uma temperatura ausente é indicada como indisponível, enquanto zero continua sendo um valor válido.
- **Lista de Processos Ativos:** Visualização limpa em tabela de processos consumindo a GPU.
- **Inventário de sensores:** categorias com cabeçalhos compactos que podem ser expandidos ou recolhidos, linhas de sensores e instâncias identificadas por recuo e colunas alinhadas para atual, mínimo, máximo e média. Nomes e valores longos podem ser consultados pela dica ao passar o mouse; em espaços estreitos, a tabela e a lista de monitores permitem rolagem horizontal. As cores acompanham o tema e a paleta personalizada.
- **Diagnóstico:** view no próprio app com os sensores detectados em tempo real e os erros recentes (port do `DebugWindow`) — ver [diagnostico.md](diagnostico.md).
- **Aviso de elevação:** aviso em âmbar com a ação de reiniciar como administrador, quando aplicável.


A interface desktop usa estilos compartilhados para ações, campos e textos secundários. Os controles de monitoramento são secundários às ações principais. Os quatro cards menores passam de quatro para duas colunas quando o espaço disponível fica abaixo de 760 unidades lógicas; o conteúdo da página continua rolável. Esses estilos não são incluídos nas janelas do overlay em jogo.

## Auto-inicialização

O monitoramento inicia automaticamente ao abrir o aplicativo (com fallback seguro e suporte a controle manual via "Iniciar Monitor" e "Parar"). A pausa (P6.7) é só de runtime — via bandeja ou atalho global — e reiniciar o app retoma a coleta.

## Cores e Tipografia

- **Paleta Base:** Neutros escuros (`#0F1117` para fundo, `#141720` para sidebar, `#181B24` para cards com borda 1px `#232734`).
- **Tipografia Principal:** Textos em `#F1F5F9` (alto contraste sem saturação agressiva), rótulos e legendas secundárias em `#94A3B8`.
- **Acentos Funcionais:** Azul royal (`#2563EB` / `#3B82F6`) para ações principais e barras de progresso.
- **Faixas de Temperatura e Uso Dinâmico:**
  - Uso < 75%: Branco/Slate neutro (`#F1F5F9`) para uso normal.
  - Uso 75% a 90%: Âmbar suave (`#F59E0B`).
  - Uso ≥ 90%: Coral (`#EF4444`).
  - Temperatura: Pílulas discretas com tons esmeralda (≤ 65°C), âmbar (≤ 80°C) ou coral (> 80°C).
- **Tokens de tema:** no app único, as cores e os acentos das
  views moram em dicionários de tema (`src/PhantomMonitor.App/Themes/Tokens.*.axaml`,
  49 chaves espelhadas entre os dois temas) e são consumidas via `{DynamicResource}`
  (as faixas dinâmicas usam brushes resolvidos por chave no ViewModel — mesmo
  catálogo). A F1.6 fecha os dois temas builtin: `Neptune` deriva da paleta
  original do Avalonia e `Slate` da paleta do WPF aposentado
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
  O card **INTERFACE** permite personalizar cinco cores-base por tema e, no modo
  avançado, substituir qualquer uma das 49 chaves. As paletas ficam separadas
  para Neptune e Slate; as substituições individuais prevalecem sobre as cores
  da família. **Concluído** mantém o rascunho e **Salvar e aplicar** persiste e
  aplica a seleção somente depois que o config for salvo. O accent principal
  também atualiza os botões de ação e o indicador de navegação selecionada,
  independentemente do accent do Windows.

## Elevação de privilégio

Se o aplicativo não estiver rodando como Administrador, exibe um banner com botão para reiniciar com privilégios elevados (necessário para a leitura irrestrita da CPU e rastreamento via PresentMon). O processo elevado aguarda a instância atual liberar o bloqueio de instância única antes de abrir a janela, evitando que o próprio reinício seja encerrado como duplicado.
