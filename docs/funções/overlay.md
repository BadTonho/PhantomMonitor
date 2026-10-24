# OverlayWindow

> **Idioma:** Português (Brasil) · [English](../en/functions/overlay.md)

## Overlay flutuante compacto

O app único oferece o overlay flutuante independente da janela principal. Ele é controlado por `showOverlay` e pelo botão **Mostrar/Ocultar overlay** da visão geral. Cada categoria é uma janela transparente independente, o que permite posicionar os blocos sem cobrir toda a tela com uma janela nativa. Se um bloco for fechado pelo sistema, o overlay inteiro é desativado e pode ser reaberto pelo dashboard.

O botão **Abrir editor do overlay** abre um canvas escuro com grade e prévia ao vivo. Clique com o botão direito em uma área vazia para criar um bloco; arraste com o botão esquerdo para movê-lo; sem modificadores, o bloco se alinha à grade de 20 px e, ao passar perto de outro bloco, às bordas dele. Mantenha Shift pressionado durante o arraste para posicioná-lo livremente; ao soltar Shift, o alinhamento volta a valer. Os limites consideram o tamanho renderizado de cada bloco, mantendo-o dentro do canvas. Dê duplo clique para escolher várias categorias, séries individuais ou padrões, gráficos e templates. O menu do botão direito sobre um bloco permite editar ou removê-lo. Gráficos e templates podem ser criados e editados no seletor. O botão **Configurações**, no canto inferior direito do editor, abre um painel à direita com animação da esquerda para a direita; a janela se expande para acomodá-lo sem cobrir nem reduzir o canvas. O painel reúne largura, opacidade geral, cor e opacidade do fundo, família e tamanho da fonte, modo compacto e borda. Ative ou desative a borda, escolha uma cor pela paleta ou informe o HEX; limpar a cor restaura a borda do tema. A opacidade geral afeta o bloco inteiro; a opacidade do fundo se combina com ela e altera apenas o preenchimento. Escolha uma cor pela paleta ou informe o HEX; limpar a cor restaura o tema. Escolha uma fonte instalada ou digite uma família; sem escolha, a tipografia atual é preservada. As alterações ficam como rascunho: **Salvar e aplicar** grava e atualiza o overlay em execução; fechar o editor descarta as mudanças não salvas de aparência, conteúdo e posição.

Na seção **Ordem no bloco**, os conteúdos escolhidos aparecem em uma lista; mover um item para cima ou para baixo altera a ordem no overlay. Categorias são grupos. Selecione uma categoria ou série e escolha uma linha para editar seu nome e cor; séries individuais também permitem precisão automática ou de 0 a 3 casas decimais.
As posições de `overlay.blocks` são normalizadas entre 0 e 1 em relação à janela monitorada no Windows ou à área disponível no Linux. Configurações antigas sem `blocks` são migradas preservando categorias ativadas, séries por instância, gráficos, templates e posições existentes; quando não há posições, os blocos começam em uma coluna vertical. Instalações novas também começam com os blocos padrão atuais. A largura (140–700 px), opacidade geral (10–100%), cor do fundo (#RRGGBB), opacidade do fundo (0–100%), família e tamanho da fonte (8–20 pt), modo compacto, visibilidade da borda e cor da borda são ajustes globais disponíveis em **Configurações → Overlay flutuante** e no painel do editor. Os valores editados no editor só são persistidos ao clicar em **Salvar e aplicar**.

O item **Gráfico de frametime** pode ser combinado com outros conteúdos em qualquer bloco. A sparkline dourada aparece quando há pelo menos duas amostras; mede 24 px no modo normal e 18 px no compacto, acompanha as amostras e congela durante a pausa.

As linhas do overlay são renderizadas como **blocos por hardware** (mesma ideia de um OSD estilo editor de jogos): rótulo pequeno no topo (`CPU`, `GPU`, `RAM`, `DISCO`, `REDE`, `VENTOINHA`, `PLACA`, `JOGO` ou a chave da série por instância) e o valor em destaque abaixo (monospace por padrão; a família personalizada vale para todos os textos), com quebra de linha para conteúdos longos; a sparkline de frametime vive dentro do bloco JOGO.

O conteúdo e a posição de cada bloco são independentes; séries, gráficos e templates podem ser combinados com categorias no mesmo bloco. O perfil por aplicativo ainda pode substituir largura, tamanho da fonte, opacidade geral e acento, além de filtrar séries por instância. A cor e a opacidade do fundo, a borda e a família tipográfica permanecem globais. No Windows, o overlay acompanha a janela ativa e mantém clique-through; no Linux, posição inicial e sobreposição em tela cheia dependem do compositor.

Internamente, o roteamento de posicionamento fica separado das implementações de Windows e Linux (`OverlayWindow.Positioning.cs`, `OverlayWindow.Windows.cs` e `OverlayWindow.Linux.cs`). Isso mantém as regras de foco e clique-through do Windows isoladas do posicionamento limitado pelo compositor no Linux.

O seletor de conteúdo é uma janela AXAML própria, com estado e operações em `OverlayBlockContentDialogViewModel`; a View só encaminha as ações dos controles. Gráficos, templates e seleção múltipla continuam sendo um rascunho até o usuário aplicar o diálogo e depois salvar o layout.

No seletor de conteúdo do bloco, filtre as séries detectadas por CPU, GPU, RAM, Disco, Rede, Ventoinhas, Placa-mãe, Bateria, FPS ou Outros; **Todas** mostra todas de uma vez. A caixa **Selecionar todas as séries exibidas** seleciona ou limpa em lote as séries do filtro atual e indica uma seleção parcial. Essa seleção afeta apenas as séries individuais, sem ativar o atalho de resumo da categoria. Também é possível adicionar uma chave ou padrão (`Gpu*`, `*Total`, `CpuTemperature#*`); padrões sem correspondência permanecem no bloco e passam a exibir dados quando a série aparecer. Séries já selecionadas e padrões manuais são preservados ao trocar o filtro. `overlay.shownSeries` é mantido para compatibilidade e migra para os itens de série dos blocos na primeira gravação. As séries são ordenadas por tipo e índice numérico, incluem chave e rótulo do hardware, exibem zero quando a leitura está presente e são limitadas a 24 linhas por bloco.

O overlay recebe os snapshots do monitor; valores indisponíveis aparecem como `—`, e a pausa mantém a última leitura. As linhas são reconciliadas por identidade e permanecem agrupadas em blocos independentes. No **Windows**, cada bloco é clique-through (`WS_EX_TRANSPARENT`) e acompanha a janela em foco a cada 30 ms; os perfis e `overlayPosition` deslocam o conjunto. No **Linux**, os blocos são janelas individuais com Topmost, e a posição inicial e a sobreposição em tela cheia dependem do compositor. O matcher de perfis continua inativo no Linux; templates e gráficos são lidos no arranque.

O app único oferece o OSD avançado descrito abaixo, incluindo templates, séries por instância, perfis por aplicativo, gráficos, clique-through no Windows e acento por perfil. A composição de blocos, séries, gráficos e templates pode ser feita na janela *Editor do overlay flutuante*; perfis por aplicativo continuam em `config.json`.

Janela de OSD (On-Screen Display) transparente sobreposta à tela, com visual clássico de OSD de jogos. É compacta, clicável (click-through), sem impacto visual excessivo sobre os jogos e sempre visível no topo.

## Estilo Visual Phantom OSD

- **Layout Inline Compacto:** Elimina cards volumosos e barras de progresso grossas. Cada dispositivo ocupa uma única linha com alinhamento tabular perfeito.
- **Cores Oficiais por Dispositivo:**
  - `GPU`: Laranja clássico (`#FF8A00`) com temperatura e uso
  - `CPU`: Ciano elétrico (`#00D2FF`) com temperatura e uso
  - `RAM`: Roxo suave (`#C084FC`) com memória em uso e porcentagem
  - `DSK`: Azul ardósia (`#60A5FA`) com taxas de leitura e escrita
  - `NET`: Verde esmeralda (`#34D399`) com download (↓) e upload (↑)
  - `FAN`: Rosa (`#F472B6`) com ventoinha da GPU e do sistema (% ou RPM)
  - `MB`: Cinza claro (`#CBD5E1`) com a maior temperatura de placa-mãe/SuperIO
  - `FPS`: Verde vibrante (`#00FF88`) com FPS e Frametime em milissegundos
- **Sublinhas de Detalhes (CPU/GPU):** Sob as linhas de CPU e GPU, uma linha secundária compacta (fonte base −2 pt) exibe:
  - CPU: clock, potência e voltagem (ex.: `4.15 GHz  65 W  1.250 V`)
  - GPU: VRAM usada/total, clock core/memória, potência, voltagem, hotspot e temp de VRAM (P8.6) (ex.: `VRAM 7.2/12 GB  2.10 GHz  7.00 GHz  220 W  1.050 V  HS 75°C  MEM 78°C`)
  - A sublinha fica oculta quando nenhum dos valores está disponível.
- **Linhas Opcionais com Auto-Revelação:** NET, FAN e MB só aparecem depois do primeiro dado real do sensor — sem poluir o OSD em máquinas que não expõem esses sensores. A revelação é só de entrada: uma vez visível, a linha fixa permanece (mostra `--` se o dado sumir); as linhas *por instância* é que se escondem de novo.
- **Linhas por Instância (Séries Dinâmicas):** além das linhas fixas, o OSD cria linhas por instância no primeiro snapshot em que surgirem, com tags próprias e cor da família:
  - `GPU1`/`GPU2...` — multi-GPU: temp, uso, VRAM, clocks, hotspot, temp de VRAM (P8.6), potência (a dedicada é a GPU 1);
  - `FAN1..` — ventoinhas por slot da placa-mãe/SuperIO;
  - `DSK1..` — uso, R/W, temperatura e saúde/endurance por disco;
  - `CPU1..` — temperatura, uso e clock por núcleo (desligado por padrão, poluído em CPUs com 32 núcleos);
  - `MB1..`, `VRM1..`, `RL1..` — temperaturas por sensor, zonas VRM e rails de tensão;
  - `NET1..` — rede por interface com velocidade e totais da sessão.
  - Filtradas pelos itens de série dos blocos (perfis podem aplicar sua whitelist `shownSeries`), ordenadas por família (GPU → ventoinhas → discos → RAM → CPU → rede → bateria → demais famílias: placa-mãe/VRM/rails/outros) e limitadas a 24 linhas por bloco; hardware que some esconde a linha até reenumerar.
- **Estatísticas de FPS:** Sublinha compacta com `AVG`, `1% Low`, `0.1% Low` e `MAX` (quebra em linha automaticamente se não couber)
- **Gráfico de Frametime Slim:** Mini-gráfico de 24px (18px no modo compacto) com linha guia de 60 FPS (16.6ms) para detecção instantânea de stuttering
- **Tipografia:** Fonte monoespaçada (`Consolas / Cascadia Mono`) para evitar que os números fiquem oscilando ou pulando de posição na tela
- **Cores Dinâmicas:** Alerta automático de temperatura e uso (Normal / Amarelo / Vermelho)

## Editor visual do overlay

O editor usa um canvas com grade e prévia ao vivo. Clique direito em uma área vazia cria um bloco; botão esquerdo arrasta com snap de 20 px e encaixe nas bordas dos blocos próximos (ou livremente segurando Shift), respeitando o tamanho do bloco e os limites do canvas; duplo clique abre o seletor multi-item; botão direito sobre um bloco abre as ações de edição e remoção. As posições normalizadas `x`/`y` vão de 0 a 1 e usam a janela monitorada como referência no Windows e a área disponível no Linux. Conteúdo e posição ficam em rascunho até **Salvar e aplicar**. Configurações antigas são migradas de `layout` e demais opções legadas para `blocks`; sem posições salvas, os blocos começam na ordem vertical padrão.

## Configuração

Configuração atual por `overlay.blocks` e aparência global na página **Configurações → Overlay flutuante**. Campos antigos permanecem para compatibilidade e migração:
- `width` - Largura da janela
- `showCpu/Gpu/Ram/Disk/Fps/ShowFrametimeGraph` - Alternar visibilidade de cada linha
- `showNetwork` - Linha de rede (`NET`) com ↓/↑ (oculta até o primeiro dado real)
- `showFans` - Linha de ventoinhas (`FAN`): GPU e sistema (oculta até o primeiro dado real)
- `showMotherboard` - Linha de placa-mãe (`MB`) com a maior temperatura (oculta até o primeiro dado real)
- `shownSeries` - Whitelist (padrões wildcard) das séries por instância; padrão `["Gpu*", "FanSpeed*", "Disk*"]` (ver seção "Linhas por Instância" acima)
- `layout` - Mapa de posições normalizadas dos blocos (`cpu`, `gpu`, `ram`, `disk`, `network`, `fans`, `motherboard`, `fps`, `extras`), cada uma com `x` e `y` entre 0 e 1.
- `blocks` - Blocos com `{ id, x, y, items: [{ kind, value }] }`; `kind` aceita categoria, série, gráfico, template ou frametime. Vários itens podem ser reunidos em cada bloco.
- `overlay.blocks[].metricOverrides` - Ajustes opcionais por sensor e específicos de cada bloco: `identity`, `displayName`, `decimals` (0–3) e `color`.
- `compactMode` - Modo compacto: reduz paddings, espaçamento das linhas e a altura do gráfico de frametime
- `accentColor` - Cor de destaque (hex) aplicada à linha do gráfico de frametime e ao contorno do painel
- `fontSize` - Tamanho base da fonte do OSD em pt (8–20; sublinhas de detalhes/estatísticas usam fonte −2 pt)
- `fontFamily` - Família personalizada; vazio preserva a tipografia atual
- `overlayOpacity` - Opacidade geral do overlay (0.1–1; padrão: 0.85), incluindo texto e fundo
- `backgroundColor` - Cor do fundo em `#RRGGBB`; `null` acompanha o tema
- `backgroundOpacity` - Opacidade adicional do preenchimento (0–1; padrão: 1)
- `borderEnabled` - Exibição da borda (padrão: `true`)
- `borderColor` - Cor da borda em `#RRGGBB`; `null` usa a cor do tema/acento
- `overlayPosition` - Coordenadas `x` e `y` na tela
- `toggleOverlayHotkey` - Atalho global para mostrar/ocultar o OSD (ex.: `Ctrl+Shift+O`; vazio = desativado)
- `pauseMonitorHotkey` - 2º atalho global (P6.7): pausa/retoma o monitoramento (padrão `Ctrl+Alt+P`; vazio = desativado) — ver [hotkey.md](hotkey.md)
- `profiles` - Perfis por aplicativo (ver seção abaixo)

## Perfis por aplicativo (P3)

Cada entrada em `config.profiles` aplica **overrides** ao OSD quando o executável
em foco casa com `AppMatch` — sem reiniciar nada, avaliado no tick de posição
(30 ms) com cache de nomes de processo:

```json
"profiles": [
  {
    "name": "CS2",
    "appMatch": "cs2",
    "offsetX": 30,
    "offsetY": 120,
    "width": 260,
    "fontSize": 13,
    "opacity": 0.9,
    "series": ["Gpu*", "FanSpeed*", "CpuTemperature#*"],
    "accentColor": "#FFCC15",
    "visible": true
  },
  { "name": "Sem OSD no jogo X", "appMatch": "desconcertante", "visible": false }
]
```

- `appMatch`: nome do executável **sem** `.exe`/caminho, case-insensitive,
  por prefixo (`cs2` casa com `cs2.exe`). `""` ou `"*"` casam com **qualquer**
  aplicativo (perfil universal).
- `visible`: `false` **esconde o OSD por completo** enquanto o perfil casa
  (útil para jogos/menus que combinam mal com overlays ou para gravar tela
  limpa); volta sozinho quando o app em foco deixa de casar. `true`/ausente =
  visível (comportamento padrão). A opacidade de perfis é limitada ao intervalo
  0,1–1; use `visible: false` para esconder o OSD.
- Campos nulos/ausentes = usa o valor global (`overlayPosition`, `overlay.width`,
  `overlay.fontSize`, `overlay.accentColor`, `overlayOpacity`,
  `overlay.shownSeries`). A `fontSize` reescala a árvore
  inteira do OSD preservando os deltas por tipo de bloco; `series` troca a
  whitelist de séries por instância (linhas fora dela são escondidas até
  voltarem).
- **Fixar pela bandeja:** o menu do ícone de bandeja tem uma seção automática
  com `Automático (por aplicativo)` + perfis; escolher um fixa o perfil
  (sobrepõe o matcher; a fixação dura a sessão, não grava no config). O matcher
  entende o pin (`_pinnedProfileName`), ativo desde a F1.5.
- **Indicador:** com perfil ativo, o OSD mostra uma linha mínima no rodapé
  (`◉ perfil: CS2`); sem perfil, nada aparece.
- **Editor Visual do WPF (aposentado):** seção "PERFIS POR APLICATIVO" com cartões editáveis
  (nome, AppMatch com botão "Capturar" do app em foco, offsets, largura, fonte,
  opacidade, séries por instância em vírgula, acento e checkbox
  `OSD visível neste app`), adicionar/remover; seção
  "LINHAS PERSONALIZADAS (TEMPLATES)" com TextBox multilinha (uma linha de
  template por linha — a UI edita só o texto, a cor permanece via config);
  "Aplicar & Salvar" grava em `config.profiles` e `overlay.templates`. No
  Avalonia (sem editor): perfis consomem o `config.json` no arranque.

## Linhas personalizadas (templates, P6.6)

```json
"templates": [
  { "text": "VRAM {GpuMemoryUsed:N0}/{GpuMemoryTotal:N0} MB", "color": "#A3E635" },
  { "text": "Ventoinha: {FanSpeed*} RPM" },
  { "text": "Desgaste da bateria: {BatteryWearLevel:N0}%" }
]
```

- **Placeholders** resolvidos a cada ciclo: `{Chave}` casando primeiro valor dado
  a série canônica (ex.: `GpuMemoryTotal`) ou uma instância (`GpuTemperature#1`), caindo
  para a primeira instância da categoria quando a canônica não existe; wildcards
  crescem do primeiro match de `SensorKey.MatchesPattern` (`{FanSpeed*}`).
- **`{Chave:N}`** fixa N decimais (0–3; padrão 1): o texto decide a unidade —
  escreva `°C`, `MB`, `ms` etc. no literal.
- **Linha oculta** enquanto qualquer placeholder não resolver (ex.: notebook de
  bateria:{BatteryWearLevel} nunca exibe em desktop).
- **Posição:** as linhas de template formam um bloco no **fim do painel** (sempre
  depois das séries dinâmicas por instância).
- **Fonte/cor:** fonte e tamanho seguem o OSD (base −1 pt); cor por template em
  `color` (#RRGGBB/#AARRGGBB) — só editável no config, a UI edita só o texto
  (mesmíssimo texto = cor preservada ao aplicar o editor).

## Gráficos de histórico por série (P8.5)

Sparklines embutidas no OSD — mesma linguagem visual do gráfico de frametime,
com janela/eixo próprios por gráfico (paridade com o "gráfico de histórico no
OSD" das suítes de referência):

```json
"graphs": [
  { "series": "GpuTemperature" },
  { "series": "Custom#1", "height": 48, "window": 600, "color": "#A3E635" },
  { "series": "CpuCoreClock#*", "min": 2000, "max": 5500 }
]
```

| Campo | Significado |
|---|---|
| `series` | Série alvo, resolvida **como as templates**: canônica (`GpuTemperature`), com fallback para a primeira instância da categoria; explícito (`GpuTemperature#1`); wildcard (`CpuCoreClock#*`); ou nome amigável de sensor customizado (`{DeltaTemp}` → `"DeltaTemp"`) |
| `height` | Altura da sparkline em px (12–120; padrão 36) |
| `window` | Amostras do buffer circular (30–1200; padrão 240 ≈ 4 min em 1 Hz) |
| `color` | Cor da linha (#RRGGBB/#AARRGGBB); vazio = cor de acento do OSD |
| `min`/`max` | Eixo Y fixo opcional (auto quando ausentes; série "plana" ganha piso de ±0.5) |

- **Posição no painel:** logo **antes do bloco de templates**, depois das séries
  dinâmicas — ambos os containers "vivem no fim" do painel.
- **Série ausente:** a sparkline esconde até a série voltar a resolver; o buffer
  circular preserva o histórico (não zera o gráfico por um ciclo caído).
- **Eixo:** auto min/max pela janela atual, com guias tracejadas nos extremos —
  os mesmos guias do frametime; valores fixos dão escala estável (benchmark).
- **Edição:** no seletor de conteúdo do editor do overlay; alterações entram
  em vigor depois de **Salvar e aplicar**. Teto de
  **12 gráficos** (normalização descarta o excesso).
- **Porta do Avalonia:** cada entrada vira sparkline persistente com buffer
  circular próprio (`FrametimeBuffer`; amostra empurrada só quando a série
  resolve) e eixo fixo opcional (`Min`/`Max`) no `HistoryChart` estendido;
  o contorno da linha usa `color` ou o acento vigente do OSD.
- **Fontes de dado:** qualquer série publicada — inclusive `Custom#i` (P8.1) e
  `CPU/Gpu Busy` do PresentMon (P6.5).

## API Nativa

- `WS_EX_LAYERED` + `WS_EX_TRANSPARENT` para janela transparente e clicável (não rouba cliques nem foco do jogo)
- `SetWindowPos` com `HWND_TOPMOST` para manter a sobreposição sempre visível

ull usa a cor do tema/acento
