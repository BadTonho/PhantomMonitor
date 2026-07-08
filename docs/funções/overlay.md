# OverlayWindow

Janela transparente sobreposta à tela que exibe os sensores em tempo real. É clicável (click-through) e sempre visível no topo.

## Funcionalidades

- Exibição de sensores agrupados por categoria (CPU, GPU, RAM, DISCO, FPS)
- Cada sensor mostra label + valor numérico + barra de progresso
- Seção de FPS com FPS atual, frametime, estatísticas (avg/min/1% low) e gráfico de frametime
- Fundo semi-transparente escuro com bordas arredondadas
- Janela clicável (não interfere com cliques do mouse em outros aplicativos)
- Posição e opacidade configuráveis

## Configuração

Configurado via `config.json` na seção `overlay`:
- `width` - Largura da janela
- `showCpu/Gpu/Ram/Disk/Network/Fans/Fps` - Alternar visibilidade de cada seção
- `compactMode` - Modo compacto
- `accentColor` - Cor de destaque (ex: "#1E90FF")

## API nativa

- `WS_EX_LAYERED` + `WS_EX_TRANSPARENT` para janela transparente e clicável
- `SetWindowLong`/`GetWindowLong` para modificar estilos estendidos
