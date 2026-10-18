# OverlayWindow

Janela de OSD (On-Screen Display) transparente sobreposta à tela inspirada no visual clássico do **RivaTuner Statistics Server (RTSS)**. É compacta, clicável (click-through), sem impacto visual excessivo sobre os jogos e sempre visível no topo.

## Estilo Visual Phantom OSD (Estilo RTSS)

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
  - GPU: VRAM usada/total, clock core/memória, potência, voltagem e hotspot (ex.: `VRAM 7.2/12 GB  2.10 GHz  7.00 GHz  220 W  1.050 V  HS 75°C`)
  - A sublinha fica oculta quando nenhum dos valores está disponível.
- **Linhas Opcionais com Auto-Revelação:** NET, FAN e MB só aparecem depois do primeiro dado real do sensor — sem poluir o OSD em máquinas que não expõem esses sensores.
- **Estatísticas de FPS:** Sublinha compacta com `AVG`, `1% Low`, `0.1% Low` e `MAX` (quebra em linha automaticamente se não couber)
- **Gráfico de Frametime Slim:** Mini-gráfico de 24px (18px no modo compacto) com linha guia de 60 FPS (16.6ms) para detecção instantânea de stuttering
- **Tipografia:** Fonte monoespaçada (`Consolas / Cascadia Mono`) para evitar que os números fiquem oscilando ou pulando de posição na tela
- **Cores Dinâmicas:** Alerta automático de temperatura e uso (Normal / Amarelo / Vermelho)

## Editor Visual de Informações na Tela (OverlayEditorWindow)

Inspirado diretamente no editor de layouts OSD do RTSS (`Editor de informações na tela`):
- **Área de Edição com Grade (Grid Canvas):** Canvas com malha ortogonal escura onde os blocos de métricas (FPS, Gráfico de Frametime, CPU, GPU, RAM, Disco, Rede, Ventoinhas e Placa-mãe) são visualizados e manipulados.
- **Arrastar e Soltar Interativo:** Suporte a movimentação livre dos blocos com o mouse e opção de alinhamento à grade (*Snap to Grid* de 10px). Arrastar um bloco desloca **o offset global do overlay** pela mesma quantidade (os campos X/Y acompanham em tempo real e o valor é persistido ao Salvar).
- **Seleção de Camadas:** Destaque visual da camada selecionada com moldura wireframe e indicação de coordenadas em tempo real.
- **Painel Inspetor Lateral:**
  - Ajuste de coordenadas absolutas de tela (`X`, `Y`) com predefinições de cantos rápidos (Superior Esquerdo, Superior Direito, Inferior Esquerdo, Inferior Direito).
  - Seleção individual de camadas ativas (habilitar/desabilitar cada sensor ou gráfico).
  - Ajuste fino de opacidade, largura do painel, modo compacto, tamanho da fonte (8–20 pt) e cor de destaque (paleta de presets ou edição direta do `config.json`).
  - Atalho global para mostrar/ocultar o OSD em qualquer aplicativo (seção "ATALHO GLOBAL", ver [hotkey.md](hotkey.md)).
- **Aplicação Imediata:** Botão "Aplicar & Salvar" que grava as alterações no `config.json` e atualiza a sobreposição em execução sem necessidade de reiniciar o programa.

## Configuração

Configurado via `config.json` ou pelo Editor Visual:
- `width` - Largura da janela
- `showCpu/Gpu/Ram/Disk/Fps/ShowFrametimeGraph` - Alternar visibilidade de cada linha
- `showNetwork` - Linha de rede (`NET`) com ↓/↑ (oculta até o primeiro dado real)
- `showFans` - Linha de ventoinhas (`FAN`): GPU e sistema (oculta até o primeiro dado real)
- `showMotherboard` - Linha de placa-mãe (`MB`) com a maior temperatura (oculta até o primeiro dado real)
- `compactMode` - Modo compacto: reduz paddings, espaçamento das linhas e a altura do gráfico de frametime
- `accentColor` - Cor de destaque (hex) aplicada à linha do gráfico de frametime e ao contorno do painel
- `fontSize` - Tamanho base da fonte do OSD em pt (8–20; sublinhas de detalhes/estatísticas usam fonte −2 pt)
- `overlayOpacity` - Opacidade do fundo translúcido (padrão: 0.85)
- `overlayPosition` - Coordenadas `x` e `y` na tela
- `toggleOverlayHotkey` - Atalho global para mostrar/ocultar o OSD (ex.: `Ctrl+Shift+O`; vazio = desativado)

## API Nativa

- `WS_EX_LAYERED` + `WS_EX_TRANSPARENT` para janela transparente e clicável (não rouba cliques nem foco do jogo)
- `SetWindowPos` com `HWND_TOPMOST` para manter a sobreposição sempre visível
