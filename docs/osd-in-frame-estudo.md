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

## Renderers implementados

| Plataforma | APIs no código nativo | Ativação |
|---|---|---|
| Linux | Vulkan; OpenGL por GLX/EGL (OpenGL ES 3+) | Vulkan pela camada ativada para o jogo; OpenGL pela biblioteca `LD_PRELOAD` |
| Windows | Direct3D 9–12, OpenGL/WGL e Vulkan | Hooks x86/x64 nos executáveis cadastrados; camada Vulkan ativada ao iniciar pelo PhantomMonitor |

Os renderers usam uma apresentação interna independente de API e Dear ImGui
como implementação de desenho. O canal de apresentação tem leitura consistente
por sequência, valida versão e tamanho e rejeita quadros com mais de três
segundos. O diagnóstico nativo tem um canal separado por renderer e processo,
para que a camada Vulkan e um hook DXGI no mesmo jogo não sobrescrevam o estado
um do outro. Quadros ocultos, inválidos ou antigos não são desenhados.

O suporte a múltiplas apresentações ainda é restrito: o renderer Vulkan exige
uma swapchain por chamada de apresentação; o hook Windows acompanha a primeira
swapchain DXGI do processo; e o preload Linux OpenGL suporta um contexto por
processo. O Diagnóstico registra a limitação. Para EGL com OpenGL ES, é exigido
OpenGL ES 3 ou superior. O nome da família de fonte é preservado no quadro v1,
mas a versão atual do renderer usa a fonte padrão interna do Dear ImGui; tamanho,
cores e geometria são aplicados, enquanto a família selecionada no editor ainda
não é carregada pelo renderer nativo.

No Linux, o usuário poderá iniciar um executável configurado no PhantomMonitor,
que define `ENABLE_PHANTOM_OVERLAY=1`, ou copiar essa variável para as opções de
inicialização do jogo na Steam. O gating por processo também mantém disponível a
coleta FPS da camada Vulkan existente.

## Estado e validação

O código contém a camada Vulkan Linux com desenho e coleta existente de
FPS/frametime, o renderer OpenGL GLX/EGL, hooks Windows Direct3D 9–12 e WGL,
além da camada Vulkan Windows para jogos iniciados pelo PhantomMonitor. O hook
Windows escolhe o launcher x86 ou x64 pelo executável e injeta a DLL somente em
processos cadastrados; a detecção de outros launchers funciona enquanto o app
está aberto. A inicialização feita pelo botão **Iniciar** mantém o jogo suspenso
até a instalação dos hooks terminar. Falhas aparecem no Diagnóstico e não
acionam janelas flutuantes automaticamente.

O build Linux compila a camada Vulkan, o preload OpenGL e os testes nativos. O
build dos componentes Windows exige Visual Studio 2022 e Vulkan SDK; deve ser
executado em Windows e os renderers precisam de validação real em x86/x64. A
presença do código e a compilação Linux não substituem essa validação. Anti-cheats
podem rejeitar a DLL e não existe tentativa de contorno.

## Validação necessária por API

Em cada API ainda é preciso validar fullscreen e janela sem borda, resize, sete
blocos, gráficos e templates, game capture, alternância de modos,
ocultar/reabrir, quadros parciais/antigos e falhas sem fallback. No Linux,
validar X11/XWayland e Wayland quando disponível. No Windows, validar x86 e x64,
os dois caminhos de início, a lista de executáveis e a camada Vulkan. Testes com
anti-cheat devem respeitar os bloqueios existentes.
