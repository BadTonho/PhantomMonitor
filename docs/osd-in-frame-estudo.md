# OSD dentro do jogo — arquitetura e fases

> **Idioma:** Português (Brasil) · [English](en/osd-in-frame-study.md)

## Decisão

O PhantomMonitor oferecerá dois modos globais: **Janela flutuante**, que mantém
os blocos em janelas separadas, e **Dentro do jogo**, que desenha a apresentação
aplicada no quadro do jogo. O modo flutuante continua como padrão para
instalações novas e configurações antigas. A escolha dentro do jogo é explícita;
se o renderer não estiver disponível ou falhar, o app informa o motivo em
Diagnóstico e não muda automaticamente para janelas.

No Windows, o hook próprio será limitado aos executáveis que o usuário adicionar
em `overlay.inGameTargets`. Hooks podem ser recusados ou bloqueados por EAC,
BattlEye, Vanguard e outros anti-cheats. O PhantomMonitor não tentará contornar
essas proteções. O modo flutuante continua disponível nesses casos.

## Contrato entre app e renderers

O app monta a apresentação usando o conteúdo **aplicado**: ordem dos blocos e
itens, valores atuais, estilos, gráficos, templates e posições. O preview do
editor continua usando os valores de rascunho. A apresentação ativa é publicada
por um canal interno de memória compartilhada versionado. O leitor nativo tem
acesso somente de leitura e aceita um quadro apenas quando versão, sequência
estável, tamanho e timestamp são válidos. Quadros incompletos, desconhecidos ou
antigos não devem ser desenhados. O contrato não é uma API pública.

O canal v1 atual usa cabeçalho de 32 bytes e payload UTF-8 com registros
tabulados (linhas terminadas em LF). `F` contém versão do payload, visibilidade,
pausa, largura, área de referência, fontes de rótulo e valor, opacidade, cores,
borda, margens, padding, espaçamentos e altura do gráfico de frametime. `B`
contém id Base64, posição normalizada e título Base64. `N`/`T` carregam tamanho
de fonte, cor e texto Base64; `G` carrega altura, cor, eixo opcional e até 1200
amostras; `E` termina o bloco.
O cabeçalho marca a hora de publicação e a sequência; o app renova o timestamp
a cada segundo enquanto o modo estiver ativo, inclusive durante pausa; leitores
devem rejeitar publicação com mais de três segundos de idade. O nome POSIX fica
persistentemente disponível para que um jogo já aberto receba quadros depois de
reiniciar o app; no encerramento normal o app publica estado oculto. O app
mantém o objeto `/phantommonitor_overlay_v1_<uid>` somente leitura para outros
processos enquanto escreve pelo mapeamento que já abriu.
Uma implementação que não reconheça a versão deve ignorar o quadro.

## Matriz de implementação

| Fase | Linux | Windows |
|---|---|---|
| Base | Vulkan | Direct3D 11 e 12 |
| Expansão | OpenGL | OpenGL, Vulkan e Direct3D 9 e 10 |
| Meta | Vulkan e OpenGL | Direct3D 9–12, Vulkan e OpenGL |

Cada API precisa interceptar sua apresentação e tratar seu próprio ciclo de
vida, swapchain/contexto, redimensionamento, sincronização, captura e falhas.
Camadas Vulkan seguem o modelo de interceptar a apresentação do quadro, como
MangoHud, mas o renderizador Phantom terá seu próprio protocolo e tratamento de
recursos. Não se presume que um hook de uma API cubra outra.

No Linux, o usuário poderá iniciar um executável configurado no PhantomMonitor,
que define `ENABLE_PHANTOM_OVERLAY=1`, ou copiar essa variável para as opções de
inicialização do jogo na Steam. O gating por processo também mantém disponível a
coleta FPS da camada Vulkan existente.

## Estado do código

Configuração, migração para `floating`, lista de executáveis, publicação do
quadro aplicado v1, controles de ativação Linux e diagnósticos da capacidade
estão implementados. **Os renderizadores nativos ainda não estão implementados**:
a camada Vulkan atual mede FPS/frametime, mas não consome o quadro para desenhar;
hooks Direct3D também não foram adicionados. Portanto `inGame` permanece sem
renderização nesta versão e o app informa essa limitação. A tabela acima é o
plano de entrega, não uma declaração de suporte atual.

## Validação necessária por API

Para cada fase, verificar fullscreen e janela sem borda, resize, sete blocos,
gráficos e templates, game capture, alternância de modos, ocultar/reabrir,
quadros parciais/antigos e falhas sem fallback. No Linux, validar X11/XWayland e
Wayland quando aplicável. No Windows, validar que só executáveis cadastrados
carregam o hook e que a falha é visível em Diagnóstico. Testes com anti-cheat
devem respeitar os bloqueios existentes.
