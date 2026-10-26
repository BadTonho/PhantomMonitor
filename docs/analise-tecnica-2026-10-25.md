# Análise técnica do Phantom Monitor — 25/10/2026

Base analisada: commit `471b775`, com árvore de trabalho inicialmente limpa.

A aplicação .NET e os componentes nativos Windows x64 compilam, mas existem falhas funcionais importantes no overlay, nos atalhos globais e na atualização automática. Foram identificados **21 achados: 10 de prioridade alta (P1) e 11 de prioridade média (P2)**. Não foi identificado um problema P0, independente de configuração e que bloqueie toda a aplicação.

Esta revisão combina leitura da arquitetura, código C#/C++, configuração, scripts, empacotamento e documentação com builds, testes existentes e probes isolados. Não é uma certificação de compatibilidade com todos os jogos, dispositivos ou distribuições. Nenhum arquivo de implementação foi corrigido nesta análise; os probes e artefatos ficaram em `build/`, ignorado pelo Git.

## Verificações executadas

| Verificação | Resultado e alcance |
| --- | --- |
| `dotnet build PhantomMonitor.slnx -c Release --no-restore` | Sucesso, zero erros e zero avisos. O primeiro bloqueio era de permissão para o log de build do Avalonia; com a execução autorizada, o build passou. |
| `dotnet run --project tests/PhantomMonitor.OverlayRegressionTests -c Release --no-restore` | O runner imprimiu 11 resultados `PASS`. O caso POSIX retorna imediatamente no Windows, portanto sua lógica Linux não foi exercitada. Houve aviso NU1801 sobre acesso ao índice NuGet durante essa execução. |
| CMake/MSVC, Vulkan e overlay Windows x64, Release | Compilaram a camada Vulkan, hook Direct3D/OpenGL, launcher, biblioteca comum e testes. |
| CTest nativo x64 e x86 | Uma entrada CTest passou em cada arquitetura. Isso valida o executável de testes de protocolo/layout, não apresentações reais em todas as APIs. |
| Hook e launcher Windows x86 | Compilaram. |
| Vulkan Windows x86 | Falhou com LNK2019/LNK1120 e LNK4272: foi selecionada uma biblioteca Vulkan x64 para o alvo x86. A biblioteca `Lib32/vulkan-1.lib` não está instalada no SDK disponível. É uma limitação do ambiente já prevista no README; também bloqueia a release completa pelo script atual. |
| `PhantomSensorLib`, Release x64 | Compilou, com aviso C4996 em `strncpy`. O buffer é terminado explicitamente; esse aviso sozinho não comprova overflow. |
| Auditoria NuGet, incluindo dependências transitivas | `dotnet list PhantomMonitor.slnx package --vulnerable --include-transitive --no-restore` não encontrou vulnerabilidades conhecidas nas fontes consultadas. Não abrange automaticamente o código C++ vendorizado ou o executável PresentMon. |
| Integridade do Git | Nenhum binário de build, log ou configuração local proibida encontrado entre os arquivos rastreados; `tools/PresentMon.exe` é a exceção autorizada. Busca textual não encontrou credenciais reconhecíveis. Não foi feita auditoria completa do histórico Git. |
| Linux nativo/WSL | Ubuntu foi identificado, mas a execução do build no WSL não foi autorizada. Os achados de Linux abaixo são de inspeção estática, salvo quando o algoritmo compartilhado foi reproduzido no Windows. |

Ferramentas disponíveis: .NET SDK 10.0.301, CMake 4.4.1, MSVC 19.44 e Vulkan SDK 1.4.357.0. A execução real de Vulkan foi limitada à criação/destruição de instância, dispositivo, filas e command pool em um processo de teste. Nenhum hook foi instalado em jogos para esta auditoria.

## Achados de prioridade alta

### A01 — Leitor nativo do overlay tenta escrever em memória somente leitura

**Evidência:** reproduzido no Windows x64.

**Local:** [OverlayFrameReader.cpp](../src/PhantomNativeOverlay/OverlayFrameReader.cpp), linhas 39–43, 66–71 e 151.

`FrameReader` abre o canal com `FILE_MAP_READ`, mas `atomic_sequence()` usa `InterlockedCompareExchange64` sobre esse endereço. Essa operação de leitura/modificação exige acesso de escrita. Um probe chamou o parser real da biblioteca com uma página `PAGE_READONLY` e capturou **`0xC0000005`**, antes mesmo de interpretar o payload.

**Impacto:** ao receber um quadro válido, o renderer pode encerrar o processo onde está carregado. Os `catch (...)` C++ usados nos hooks não garantem captura dessa exceção de acesso no build MSVC atual.

**Correção indicada:** usar uma carga atômica compatível com um mapeamento somente leitura e com x86/x64. Adicionar teste sobre página/mapeamento realmente protegido contra escrita; os testes atuais usam buffers graváveis.

### A02 — Command buffers criados pela camada Vulkan ficam sem despacho do loader

**Evidência:** padrão de criação reproduzido com Vulkan real em processo isolado.

**Local:** [PhantomVulkanOverlay.cpp](../src/PhantomVulkanLayer/PhantomVulkanOverlay.cpp), linhas 321, 426 e 504; [PhantomVulkanLayer.cpp](../src/PhantomVulkanLayer/PhantomVulkanLayer.cpp), linha 390; backend ImGui Vulkan, linha 453.

Os command buffers são alocados pela função da próxima camada, sem inicialização de loader data. Depois, o backend ImGui chama funções Vulkan globais com esses handles. O probe repetiu essa combinação: a alocação funcionou, o dispatch do command buffer diferiu do dispatch do dispositivo e uma chamada pelo loader produziu **`0xC0000005`**.

**Impacto:** crash ao desenhar o overlay, mesmo após resolver o carregamento da DLL e a leitura do canal.

**Correção indicada:** obter `VK_LOADER_DATA_CALLBACK`, inicializar os objetos criados pela camada e resolver as funções do backend de maneira consistente com a cadeia de despacho. O requisito de inicialização dos objetos dispatchable está no [contrato do Vulkan Loader](https://github.com/KhronosGroup/Vulkan-Loader/blob/main/docs/LoaderLayerInterface.md#creating-new-dispatchable-objects).

### A03 — Manifesto Vulkan Windows não carrega a biblioteca no loader testado

**Evidência:** reproduzido com o manifesto e a DLL lado a lado, sem alterar o Registro.

**Local:** [phantom_vulkan_layer_windows.json](../src/PhantomVulkanLayer/phantom_vulkan_layer_windows.json), linha 6.

O manifesto usa `"library_path": "./VkLayer_phantom_monitor.dll"`. No loader disponível, esse caminho falhou com **erro Windows 87**; `vkCreateInstance` com a camada explicitamente solicitada devolveu `-1`. Sem a camada, a criação funcionou. Em uma cópia do manifesto, usar `.\VkLayer_phantom_monitor.dll` ou caminho absoluto permitiu criar instância e dispositivo.

**Impacto:** o app pode informar registro bem-sucedido enquanto a camada efetivamente não carrega. O resultado exato em outros loaders precisa ser validado; a incompatibilidade está confirmada neste ambiente.

**Correção indicada:** gerar o caminho Windows com separadores adequados e testar o manifesto instalado, além de verificar sua presença no disco.

### A04 — Atalhos globais são registrados na thread errada

**Evidência:** reproduzido, retorno falso e **Win32 1408**.

**Local:** [GlobalHotkeyService.cs](../src/PhantomMonitor.Core/Services/Hotkeys/Windows/GlobalHotkeyService.cs), linhas 78, 107 e 122.

A janela message-only é criada na thread `RunMessageLoop`, enquanto `Register()` chama `RegisterHotKey` na thread do chamador. O teste com `Ctrl+Alt+Shift+F24` falhou com erro 1408. O log sugere conflito com outro aplicativo, embora o problema seja a associação entre threads.

**Impacto:** atalhos de pausa e alternância do overlay não funcionam pelo fluxo normal de inicialização.

**Correção indicada:** encaminhar registro e remoção para a thread proprietária da janela. A restrição é explícita na [documentação da Microsoft](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey).

### A05 — Atualização Windows não instala nem atualiza as subpastas dos renderers

**Evidência:** demonstrado pelo fluxo de empacotamento e pelo script gerado.

**Local:** [WindowsUpdateApplier.cs](../src/PhantomMonitor.Core/Services/Update/WindowsUpdateApplier.cs), linha 180; [release.ps1](../scripts/release.ps1), etapa de build dos renderers.

A release inclui `PhantomOverlay/x64/` e `PhantomOverlay/x86/`, mas o updater percorre somente arquivos na raiz do staging e faz `copy` por arquivo. Ele não replica a árvore de diretórios.

**Impacto:** instalações antigas não recebem os renderers novos; instalações atuais mantêm hooks, launchers e camada Vulkan antigos ao atualizar o executável .NET. Isso cria incompatibilidade entre versões dos protocolos e do renderer.

**Correção indicada:** atualizar o payload completo, incluindo subpastas, com manifesto de arquivos e tratamento de DLLs em uso por jogos. Testar atualização a partir de uma versão sem `PhantomOverlay/`.

### A06 — Atualização AppImage procura o download em uma pasta diferente

**Evidência:** divergência determinística entre os caminhos do produtor e do consumidor.

**Local:** [LinuxUpdateApplier.cs](../src/PhantomMonitor.Core/Services/Update/LinuxUpdateApplier.cs), linhas 55 e 96.

`PrepareAsync()` grava o AppImage em `<stagingRoot>/<asset>`. `Apply()` procura `<stagingRoot>/<version>/<asset>`. O ramo AppImage não move o arquivo para o diretório de versão.

**Impacto:** o download termina, mas a aplicação da atualização falha por payload inexistente. O estado `Applying` já foi publicado, e a UI agenda o encerramento, agravando a falha.

**Correção indicada:** compartilhar uma única função de resolução do payload entre preparação e aplicação e validar tudo antes de publicar o estado que encerra a aplicação.

### A07 — Descoberta tardia impede o renderer D3D12 de obter a fila da swapchain

**Evidência:** confirmado pela ordem do fluxo; não foi executado jogo D3D12.

**Local:** [MainWindowViewModel.OverlayInGame.cs](../src/PhantomMonitor.App/ViewModels/MainWindowViewModel.OverlayInGame.cs), linhas 162 e 245; [PhantomOverlayWindows.cpp](../src/PhantomNativeOverlay/windows/PhantomOverlayWindows.cpp), linhas 397, 607 e 615.

A aplicação injeta o hook depois que o PresentMon observa apresentações. A swapchain do jogo já existe nesse momento. A associação entre swapchain e `ID3D12CommandQueue` só é registrada nos hooks de criação de novas swapchains. Para a swapchain existente, `find_queue()` retorna nulo e `ensure_d3d12()` recusa desenhar.

**Impacto:** o fluxo automático anunciado para D3D12 pode instalar o hook, mas não desenha até uma recriação compatível da swapchain. Reiniciar o jogo com a mesma descoberta tardia não resolve necessariamente. O launcher antecipado existe no C++, mas a UI atual oferece lançamento apenas no Linux.

**Correção indicada:** definir uma estratégia efetiva para capturar a fila em processos já iniciados ou disponibilizar inicialização antecipada compatível com o fluxo do produto.

### A08 — Consulta Vulkan por instância pode desviar dos hooks

**Evidência:** reproduzido com cadeia simulada e os exports reais da DLL.

**Local:** [PhantomVulkanLayer.cpp](../src/PhantomVulkanLayer/PhantomVulkanLayer.cpp), linha 586.

`Phantom_vkGetInstanceProcAddr` devolve hooks para poucas funções e encaminha `vkQueuePresentKHR`, `vkGetDeviceQueue`, `vkCreateSwapchainKHR`, `vkDestroyDevice` e `vkGetDeviceProcAddr` para a próxima camada. Essas consultas podem obter funções que não passam pelas interceptações do Phantom. O probe confirmou ponteiros diferentes dos hooks oferecidos pelo export de dispositivo.

**Impacto:** a compatibilidade depende de como o jogo resolve as funções; telemetria, registro de filas/swapchains e limpeza podem ser ignorados.

**Correção indicada:** centralizar a tabela de interceptações e usá-la nas consultas pertinentes por instância e por dispositivo. A possibilidade de consultar comandos de dispositivo por instância é descrita na [arquitetura oficial do loader](https://github.com/KhronosGroup/Vulkan-Loader/blob/main/docs/LoaderInterfaceArchitecture.md).

### A09 — Metadados de filas Vulkan são indexados pelo dispatch do dispositivo

**Evidência:** uso incorreto confirmado no código; propriedade dos handles reproduzida com Vulkan real.

**Local:** [PhantomVulkanOverlay.cpp](../src/PhantomVulkanLayer/PhantomVulkanOverlay.cpp), linhas 528 e 587.

`queue_families` usa `dispatch_key(queue)` como identidade da fila. Duas filas distintas do mesmo dispositivo podem compartilhar esse ponteiro. O probe obteve duas filas de famílias diferentes: `distinct_queue_handles=1; shared_dispatch_key=1`.

**Impacto:** registrar uma fila sobrescreve a família da outra. A checagem de suporte gráfico e a escolha do command pool podem usar uma família incorreta, causando falha de desenho ou submissão inválida.

**Correção indicada:** indexar os metadados específicos de fila pelo próprio `VkQueue`; reservar o dispatch key para identificar o dispositivo.

### A10 — Rollback do updater não restaura o conjunto completo

**Evidência:** confirmado nos scripts gerados; não foram executadas trocas de instalação.

**Local:** [WindowsUpdateApplier.cs](../src/PhantomMonitor.Core/Services/Update/WindowsUpdateApplier.cs), linhas 178–212; [LinuxUpdateApplier.cs](../src/PhantomMonitor.Core/Services/Update/LinuxUpdateApplier.cs), linhas 154–177.

No Windows e no portable Linux, uma falha após algumas cópias não desfaz as cópias anteriores; a tentativa de restauração cobre apenas o arquivo que falhou. Backups existentes são apagados no começo da próxima tentativa. O ramo AppImage sobrescreve o arquivo diretamente, sem backup.

**Impacto:** uma falha de disco, permissão ou arquivo em uso pode deixar uma instalação com versões misturadas e sem recuperação automática completa, apesar da promessa de rollback.

**Correção indicada:** implementar troca transacional do conjunto ou journal de operações com restauração integral. Validar o novo payload antes de descartar backups.

## Achados de prioridade média

### A11 — Regras de alerta independentes compartilham estado indevidamente

**Evidência:** reproduzido no Core: duas regras configuradas, apenas um disparo.

**Local:** [AlertRuleEvaluator.cs](../src/PhantomMonitor.Core/Services/Alerts/AlertRuleEvaluator.cs), linhas 36 e 81.

A identidade usa somente o hash de sensor, índice, comparação e limite. Regras com a mesma condição e ações diferentes compartilham `_active` e cooldown. Exemplo: uma regra de popup e outra de script em CPU > 80 °C resultam em apenas uma ação. Além disso, um hash inteiro não deve substituir uma identidade sem tratamento de colisões.

**Correção indicada:** identificar cada regra separadamente, por ID persistido ou identidade estável própria.

### A12 — Seleção de processos e reset do contador de FPS Linux são frágeis

**Evidência:** confirmado pela leitura de `PollTelemetry()`; não exercitado em Linux.

**Local:** [LinuxFpsMonitor.cs](../src/PhantomMonitor.Core/Services/Sensors/Linux/LinuxFpsMonitor.cs), linhas 129–176 e 226.

O leitor aceita o primeiro objeto válido e sai do laço sem escolher o processo com quadros recentes. Um jogo minimizado/ocioso, mas vivo, pode bloquear a leitura de outro jogo ativo. Quando troca de PID, limpa algumas estatísticas, mas mantém `_lastFrameCount`. Um processo novo com contador menor pode ficar sem amostras até ultrapassar o contador anterior. A mesma situação pode ocorrer com recriação da instância Vulkan no mesmo PID.

**Correção indicada:** selecionar por atividade/frescor, manter estado por processo/sessão e reiniciar os contadores relacionados quando a origem muda ou reinicia. Ao expirar, atualizar também a disponibilidade dos dados.

### A13 — Estatísticas de FPS Linux perdem a maioria dos quadros

**Evidência:** comportamento determinado pelo protocolo e pela frequência de leitura.

**Local:** [LinuxFpsMonitor.cs](../src/PhantomMonitor.Core/Services/Sensors/Linux/LinuxFpsMonitor.cs), linhas 91, 174 e 202; [PhantomVulkanTelemetry.h](../src/PhantomVulkanLayer/PhantomVulkanTelemetry.h).

A camada substitui o último frametime a cada apresentação. O leitor consulta a cada 100 ms e registra só o último quadro observado, mesmo se o contador avançou muitas posições. Em 60 FPS, recebe aproximadamente um frametime de cada seis; em 240 FPS, um de cada 24. Os lows e o histórico podem perder travamentos inteiros entre polls. A janela de 3.600 amostras representa aproximadamente seis minutos em 10 Hz, não os cerca de 60 segundos mencionados no comentário.

**Correção indicada:** publicar um ring buffer de frametimes com sequência ou agregados calculados por apresentação, preservando todos os eventos necessários para os percentis.

### A14 — Série que reaparece pode receber um valor antigo

**Evidência:** reproduzido: entrada reapareceu em 90, mas a saída foi 30.

**Local:** [SensorTuningEngine.cs](../src/PhantomMonitor.Core/Services/Sensors/SensorTuningEngine.cs), linhas 84–110.

O estado de cadência e suavização de uma série não é removido quando ela desaparece do snapshot. Se retornar antes de `NextDue`, recebe `LastPublished` da presença anterior. Isso contradiz o contrato documentado de primeira amostra fresca após ausência.

**Correção indicada:** limpar o estado das séries ausentes ou acompanhar explicitamente transições de disponibilidade, inclusive a janela de suavização.

### A15 — CSV descarta sensores que surgem após a primeira amostra

**Evidência:** reproduzido: depois de surgir FPS, o cabeçalho continuou `Timestamp,CpuTemperature`.

**Local:** [CsvLogger.cs](../src/PhantomMonitor.Core/Services/Logging/CsvLogger.cs), linhas 52 e 61.

As colunas são congeladas no primeiro snapshot. Sensores com aquecimento, jogos abertos depois do início da gravação e dispositivos conectados posteriormente não são incluídos. Isso é particularmente relevante para FPS, que normalmente ainda não existe quando a aplicação inicia.

**Correção indicada:** definir uma política explícita de evolução do esquema, como rotação para novo arquivo ao surgirem colunas, ou formato com registros por série. Usar JSONL quando a captura precisa preservar séries dinâmicas.

### A16 — `null` em gráficos/templates faz o carregamento abandonar toda a configuração

**Evidência:** `Normalize()` com um gráfico nulo reproduziu `NullReferenceException`.

**Local:** [AppConfig.Normalize.cs](../src/PhantomMonitor.Core/Services/Configuration/AppConfig.Normalize.cs), linhas 88–93; [Config.cs](../src/PhantomMonitor.Core/Services/Configuration/Config.cs), tratamento de exceções em `Load()`.

A normalização acessa `graph.Id` e `template.Id` antes de remover elementos nulos, embora a remoção exista mais adiante. Um JSON sintaticamente válido com `graphs: [null]` ou `templates: [null]` causa exceção e `Load()` retorna uma configuração padrão inteira.

**Impacto:** o usuário perde as opções efetivas da sessão e pode sobrescrever sua configuração original ao salvar.

**Correção indicada:** filtrar nulos antes da primeira enumeração e preservar/diagnosticar entradas inválidas sem descartar opções independentes.

### A17 — Totais de rede Linux superestimam tráfego depois de períodos ociosos

**Evidência:** confirmado pelo cálculo e pela posição da atualização do relógio.

**Local:** [LinuxDiskNetworkReader.cs](../src/PhantomMonitor.Core/Services/Sensors/Linux/LinuxDiskNetworkReader.cs), linhas 151–165.

`_lastNetSampleUtc` só avança quando existe tráfego. Após vários ciclos de zero bytes, a próxima velocidade, calculada sobre o intervalo do último tick, é multiplicada por todo o período desde a última amostra com tráfego. Por exemplo, um intervalo final de 100 bytes/s pode ser contado como 1.100 bytes após dez segundos ociosos.

**Correção indicada:** acumular deltas de bytes dos contadores ou atualizar a referência temporal em todos os ciclos válidos, incluindo zeros.

### A18 — Filtro de discos exclui dispositivos Linux legítimos

**Evidência:** confirmado pela expressão de `IsPhysicalDisk()`.

**Local:** [LinuxDiskNetworkReader.cs](../src/PhantomMonitor.Core/Services/Sensors/Linux/LinuxDiskNetworkReader.cs), linha 94.

São aceitos apenas nomes `sd` com uma letra e parte dos nomes NVMe. Dispositivos como `vda`, `xvda`, `mmcblk0` e `sdaa` ficam fora da coleta de throughput. Isso afeta VMs, eMMC e máquinas com muitas unidades.

**Correção indicada:** enumerar dispositivos em `/sys/block` e distinguir partições por metadados, com política explícita para discos virtuais e device mapper.

### A19 — Link do pacote `.deb` depende de `/lib` estar unido a `/usr/lib`

**Evidência:** resolução estática do link.

**Local:** [deb.sh](../packaging/linux/deb.sh), linha 32.

O binário é instalado em `/usr/lib/phantom-monitor`, mas o link em `/usr/bin` usa `../../lib/phantom-monitor/PhantomMonitor`, resolvendo para `/lib/phantom-monitor/PhantomMonitor`. Em distribuições com `/lib -> /usr/lib`, o erro é mascarado. Sem essa união, o atalho e a entrada desktop não encontram o binário.

**Correção indicada:** usar `../lib/phantom-monitor/PhantomMonitor` ou o destino absoluto correto. Testar o pacote instalado, além da geração do arquivo `.deb`.

### A20 — Autostart de AppImage grava o caminho temporário do executável interno

**Evidência:** confirmado pela seleção de caminho; não validado em sessão AppImage.

**Local:** [LinuxStartupManager.cs](../src/PhantomMonitor.Core/Services/Platform/Linux/LinuxStartupManager.cs), linhas 56–66.

O serviço grava `Environment.ProcessPath` no `Exec=`. Em AppImage, esse executável reside no ponto de montagem temporário, enquanto o arquivo persistente está em `APPIMAGE`. Após desmontar ou reiniciar, o caminho registrado deixa de existir. O diretório autostart também está fixado em `~/.config`, ignorando `XDG_CONFIG_HOME`.

**Correção indicada:** usar o AppImage externo quando aplicável, respeitar o diretório XDG e escapar o comando conforme o formato desktop.

### A21 — Extração do updater Linux quebra com apóstrofos nos caminhos

**Evidência:** confirmado pela construção do comando de shell.

**Local:** [LinuxUpdateApplier.cs](../src/PhantomMonitor.Core/Services/Update/LinuxUpdateApplier.cs), linha 66.

Os caminhos são interpolados entre aspas simples em `/bin/sh -c`, sem escapar apóstrofos. Um diretório de dados com `'` pode quebrar a extração ou alterar a interpretação do comando.

**Correção indicada:** iniciar `tar` diretamente, com `ProcessStartInfo.ArgumentList` para cada argumento. Não há necessidade de um shell nessa operação.

## Lacunas de validação e riscos adicionais

Estes pontos merecem acompanhamento, mas não foram contabilizados como falhas reproduzidas de execução:

- **Testes fora da solução principal.** `PhantomMonitor.slnx` inclui apenas App e Core. O runner é um console separado, não um projeto de testes convencional. Um build ou `dotnet test` da solução não garante a execução dele. O runner também imprime `PASS` para o caso POSIX que foi pulado no Windows. Registrar skips explicitamente e padronizar um comando que execute todos os testes.
- **Cobertura insuficiente dos renderers.** Os testes nativos verificam protocolo/layout, mas não mapeamento Windows somente leitura, callback do loader, apresentação, resize, troca de dispositivo ou preservação de estado gráfico. Os achados A01/A02 escaparam justamente desses limites. A implementação Windows também precisa de testes específicos para `Present1`, `ResizeBuffers1`, D3D9Ex e swapchains criadas por outras entradas DXGI.
- **Swapchains Vulkan especiais.** O renderer não guarda/valida todas as condições de `imageUsage`, imagens protegidas, camadas de imagem e recriação em outra família de fila antes de criar render targets. Verificar compatibilidade e recusar caminhos não suportados com diagnóstico, preservando a apresentação do aplicativo.
- **Concorrência Vulkan/ImGui.** Há mutex por swapchain, mas o contexto ImGui global é selecionado com `SetCurrentContext`. Apresentações paralelas de swapchains distintas precisam de validação e sincronização apropriada; não foi feito stress test.
- **Fila de snapshots sem limite.** `BackgroundSnapshotWriter` usa `Channel.CreateUnbounded`. Disco lento ou writer bloqueado pode acumular snapshots indefinidamente. Definir capacidade e política de descarte/contrapressão compatíveis com cada formato de gravação.
- **HTTP sem limite de handlers.** Cada requisição cria uma tarefa e faz escrita síncrona. Com `listenAll`, clientes lentos ou excesso de requisições podem pressionar threads/memória. O acesso LAN sem autenticação/TLS é uma escolha já documentada; não foi tratado aqui como vulnerabilidade nova.
- **Integridade das atualizações.** O download compara tamanho, mas não valida assinatura do payload ou digest autenticado. HTTPS e a confiança no repositório continuam sendo a base da distribuição. A frase do README sobre binários assinados não é demonstrada pelos scripts de release; verificar se existe etapa externa real de assinatura.
- **Distribuição Linux.** Não houve build Linux, instalação de `.deb`/AppImage nem teste de dependências do sistema, permissões executáveis no tarball produzido no Windows, ícone AppDir, Proton/Steam ou jogos de 32 bits. Essas compatibilidades permanecem abertas.

## Comportamentos que não devem ser confundidos com bugs novos

- `PhantomSensorLib` é um stub documentado, retorna zero e não é carregado pelo pipeline atual. As leituras reais Windows vêm do LibreHardwareMonitor; a presença do stub não explica, por si só, sensores zerados no dashboard.
- Linux com Wayland, GPU Nouveau e sensores não expostos pelo kernel tem limitações já descritas. Ausência de leitura precisa ser comparada à fonte disponível no hardware.
- Bloqueio por anti-cheat, acesso a processo elevado e privilégios ETW são restrições documentadas; não houve tentativa de contorná-las.
- Hooks nativos x86 não significam que a aplicação desktop tenha uma release Windows x86. O empacotamento principal anunciado é x64.

## Evidências dos probes locais

Os fontes auxiliares estão em `build/audit-probes/`, sem inclusão no Git. Os resultados principais foram:

```text
Alertas independentes: esperado=2; observado=1
Serie que reaparece: esperado=90; observado=30
Normalizacao de graph nulo: NullReferenceException
CSV apos FPS aparecer: Timestamp,CpuTemperature
Atalho global: registrado=False
RegisterHotKey: Win32 1408

readonly_frame_parse=0xc0000005
vkQueuePresentKHR: gipa_matches_hook=0
vkGetDeviceQueue: gipa_matches_hook=0
vkCreateSwapchainKHR: gipa_matches_hook=0
vkDestroyDevice: gipa_matches_hook=0
distinct_queue_handles=1; shared_dispatch_key=1
allocate_below_layer=0
command_dispatch_matches_device=0
begin_without_loader_data=0xc0000005
```

O teste de alocação Vulkan reproduz o padrão problemático em um processo auxiliar; não executa todo o renderer nem afirma que todos os drivers produzirão a mesma exceção. Os testes de manifesto alteraram somente uma cópia ignorada pelo Git. Com o manifesto Windows original, a criação explícita falhou com erro de carregamento 87; com caminho Windows ou absoluto, a criação funcionou.

## Ordem de correção sugerida

1. Corrigir A01/A02 antes de ampliar testes em jogos, pois afetam a estabilidade do processo hospedeiro.
2. Resolver A03/A04 e validar os caminhos reais de carregamento e os atalhos no desktop.
3. Corrigir A05/A06/A10 e criar testes de atualização entre versões, com subpastas, arquivo em uso e falha de cópia.
4. Corrigir A07/A08/A09 e validar D3D12/Vulkan com resolução de funções por instância/dispositivo e mais de uma família de fila.
5. Corrigir alertas, confiabilidade da telemetria e configuração (A11–A18).
6. Validar os pacotes Linux e autostart (A19–A21), com execução real em distribuições suportadas.

Cada correção deve atualizar a documentação correspondente no mesmo ciclo, conforme `AGENTS.md`. Os resultados desta revisão mostram que build bem-sucedido e testes de protocolo aprovados ainda não garantem estabilidade do overlay ou integridade do updater.
