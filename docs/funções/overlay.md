# OverlayWindow

Janela de OSD (On-Screen Display) transparente sobreposta à tela inspirada no visual clássico do **RivaTuner Statistics Server (RTSS)**. É compacta, clicável (click-through), sem impacto visual excessivo sobre os jogos e sempre visível no topo.

## Estilo Visual RivaTuner (RTSS)

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

## Configuração

Configurado via `config.json` na seção `overlay`:
- `width` - Largura da janela
- `showCpu/Gpu/Ram/Disk/Fps` - Alternar visibilidade de cada linha
- `overlayOpacity` - Opacidade do fundo translúcido (padrão: 0.85)

## API Nativa

- `WS_EX_LAYERED` + `WS_EX_TRANSPARENT` para janela transparente e clicável (não rouba cliques nem foco do jogo)
- `SetWindowPos` com `HWND_TOPMOST` para manter a sobreposição sempre visível
