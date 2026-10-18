# OverlayWindow

Janela de OSD (On-Screen Display) transparente sobreposta à tela inspirada no visual clássico do **RivaTuner Statistics Server (RTSS)**. É compacta, clicável (click-through), sem impacto visual excessivo sobre os jogos e sempre visível no topo.

## Estilo Visual Phantom OSD (Estilo RTSS)

- **Layout Inline Compacto:** Elimina cards volumosos e barras de progresso grossas. Cada dispositivo ocupa uma única linha com alinhamento tabular perfeito.
- **Cores Oficiais por Dispositivo:**
  - `GPU`: Laranja clássico (`#FF8A00`) com temperatura e uso
  - `CPU`: Ciano elétrico (`#00D2FF`) com temperatura e uso
  - `RAM`: Roxo suave (`#C084FC`) com memória em uso e porcentagem
  - `DSK`: Azul ardósia (`#60A5FA`) com taxas de leitura e escrita
  - `FPS`: Verde vibrante (`#00FF88`) com FPS e Frametime em milissegundos
- **Estatísticas de FPS:** Sublinha compacta com `AVG` e `1% Low`
- **Gráfico de Frametime Slim:** Mini-gráfico de 24px com linha guia de 60 FPS (16.6ms) para detecção instantânea de stuttering
- **Tipografia:** Fonte monoespaçada (`Consolas / Cascadia Mono`) para evitar que os números fiquem oscilando ou pulando de posição na tela
- **Cores Dinâmicas:** Alerta automático de temperatura e uso (Normal / Amarelo / Vermelho)

## Editor Visual de Informações na Tela (OverlayEditorWindow)

Inspirado diretamente no editor de layouts OSD do RTSS (`Editor de informações na tela`):
- **Área de Edição com Grade (Grid Canvas):** Canvas com malha ortogonal escura onde os blocos de métricas (FPS, Gráfico de Frametime, CPU, GPU, RAM, Disco) são visualizados e manipulados.
- **Arrastar e Soltar Interativo:** Suporte a movimentação livre dos blocos com o mouse e opção de alinhamento à grade (*Snap to Grid* de 10px).
- **Seleção de Camadas:** Destaque visual da camada selecionada com moldura wireframe e indicação de coordenadas em tempo real.
- **Painel Inspetor Lateral:**
  - Ajuste de coordenadas absolutas de tela (`X`, `Y`) com predefinições de cantos rápidos (Superior Esquerdo, Superior Direito, Inferior Esquerdo, Inferior Direito).
  - Seleção individual de camadas ativas (habilitar/desabilitar cada sensor ou gráfico).
  - Ajuste fino de opacidade e largura do painel.
- **Aplicação Imediata:** Botão "Aplicar & Salvar" que grava as alterações no `config.json` e atualiza a sobreposição em execução sem necessidade de reiniciar o programa.

## Configuração

Configurado via `config.json` ou pelo Editor Visual:
- `width` - Largura da janela
- `showCpu/Gpu/Ram/Disk/Fps/ShowFrametimeGraph` - Alternar visibilidade de cada linha
- `overlayOpacity` - Opacidade do fundo translúcido (padrão: 0.85)
- `overlayPosition` - Coordenadas `x` e `y` na tela

## API Nativa

- `WS_EX_LAYERED` + `WS_EX_TRANSPARENT` para janela transparente e clicável (não rouba cliques nem foco do jogo)
- `SetWindowPos` com `HWND_TOPMOST` para manter a sobreposição sempre visível
