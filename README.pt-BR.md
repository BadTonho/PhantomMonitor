<p align="right"><a href="README.md">English</a> | Português (Brasil)</p>

<p align="center">
  <img src="fantasma.png" alt="Ícone provisório do Phantom Monitor" width="170" />
</p>

<h1 align="center">Phantom Monitor</h1>

<p align="center">
  <strong>Hardware, FPS e frametime em um só lugar.</strong><br />
  Monitoramento local para Windows e Linux, com dashboard e overlay configurável.
</p>

<p align="center">
  <img src="https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white" alt=".NET 10" />
  <img src="https://img.shields.io/badge/Windows-Avalonia-0078D4?logo=windows&logoColor=white" alt="Windows com Avalonia" />
  <img src="https://img.shields.io/badge/Linux-Avalonia-FCC624?logo=linux&logoColor=111111" alt="Linux com Avalonia" />
</p>

<p align="center">
  <a href="#recursos">Recursos</a> ·
  <a href="#comece-a-usar">Comece a usar</a> ·
  <a href="#arquitetura">Arquitetura</a> ·
  <a href="#documentacao">Documentação</a>
</p>

O **Phantom Monitor** acompanha sensores do computador e apresenta as leituras em tempo real. Ele reúne uso, temperaturas, clocks, memória, armazenamento, rede, ventoinhas e telemetria de jogos em uma interface desktop.

O projeto roda um **único app desktop Avalonia** no Windows e no Linux. Releases anteriores tinham um frontend WPF no Windows, aposentado na fase 2 do [roadmap de unificação visual](docs/roadmap-unificacao-visual.md); a tabela abaixo resume o suporte atual por plataforma.

## Recursos

| Recurso | Windows | Linux |
| --- | --- | --- |
| Sensores | CPU, GPU, RAM, discos, rede, ventoinhas e sensores da placa-mãe via LibreHardwareMonitor | Sensores de `sysfs`, `procfs` (incluindo `fdinfo` DRM), `hwmon`, zonas térmicas Linux, DRM e NVML opcional da NVIDIA; métricas de GPU variam conforme o driver |
| FPS e frametime | PresentMon via ETW | Camada Vulkan Phantom para jogos Vulkan |
| Overlay | OSD flutuante com click-through e perfis; renderizadores opcionais dentro do jogo para executáveis x86/x64 autorizados: Direct3D 9–12, OpenGL/WGL e Vulkan ao iniciar pelo PhantomMonitor | Janelas separadas e arrastáveis; renderização opcional dentro do jogo via Vulkan e OpenGL GLX/EGL; jogos OpenGL precisam da biblioteca preload instalada |
| Inventário | Inventário de hardware e estatísticas por série | Séries detectadas agrupadas por categoria; sensores sem leitura aparecem como indisponíveis |
| Alertas | Notificação, som, scripts e desligamento configurável | Toast interno, notificações do desktop e execução de scripts |
| Diagnóstico | Fontes de sensores, avisos atuais de GPU e log de erros | Fontes por placa, geometria das janelas do overlay, avisos atuais de GPU, erros recentes e detalhes da NVML NVIDIA |
| Fórmulas e ajuste por série | Compatibilidade com as configurações existentes | Editores visuais de sensores customizados e ajuste de cadência/suavização |
| Telemetria | CSV, JSON, JSONL e exportador HTTP opcional | CSV, JSON, JSONL e exportador HTTP opcional |

O suporte varia conforme hardware, API gráfica e compositor. No Linux, FPS Vulkan e desenho dentro do jogo usam a camada Phantom; OpenGL usa a biblioteca preload GLX/EGL instalada por `install_layer.sh`. No Windows, a coleta de FPS usa ETW. O modo dentro do jogo é opcional e os hooks Windows ficam restritos aos executáveis adicionados pelo usuário; anti-cheats podem bloqueá-los e o Phantom Monitor não tentará contornar essas proteções. Os renderizadores nativos Windows exigem build em Windows com Visual Studio 2022 e Vulkan SDK e ainda precisam de validação em execução no Windows.

## Comece a usar

É necessário ter o **SDK .NET 10** instalado. Consulte os requisitos detalhados em [Suporte a Linux](docs/funções/suporte-linux.md) e [Configuração](docs/funções/configuracao.md).

### Linux

Inicie o dashboard Avalonia:

```bash
dotnet run --project src/PhantomMonitor.App
```

Para coletar FPS em jogos Vulkan, compile e instale a camada Phantom:

```bash
cd src/PhantomVulkanLayer
chmod +x install_layer.sh
./install_layer.sh
```

Depois, nas opções de inicialização do jogo — por exemplo, na Steam — use:

```bash
ENABLE_PHANTOM_OVERLAY=1 %command%
```

O instalador compila a camada Vulkan e a biblioteca preload GLX/EGL. Inicie um executável cadastrado pelo botão **Iniciar** nas Configurações ou use a opção Steam gerada pelo app:

```bash
ENABLE_PHANTOM_OVERLAY=1 LD_PRELOAD="$HOME/.local/lib/libPhantomOpenGLOverlay.so${LD_PRELOAD:+:$LD_PRELOAD}" %command%
```

Vulkan usa a camada para FPS e desenho dentro do jogo. OpenGL exige a biblioteca preload e suporta GLX e EGL (OpenGL ES 3+); apenas um contexto OpenGL por processo é suportado. Em **Configurações → Modo do overlay**, `floating` continua sendo o padrão e configurações antigas migram para esse valor. Escolher `inGame` oculta as janelas flutuantes; o estado e as falhas nativas por processo aparecem em Diagnóstico. Vários swapchains Vulkan ou DXGI no mesmo processo são indicados como não suportados. Em um desktop Wayland, confira **Diagnóstico → Resumo** para ver o backend Avalonia: a configuração padrão do projeto usa X11/XWayland, enquanto um compositor Wayland nativo pode ignorar posições absolutas das janelas.

### Windows

Inicie o app:

```powershell
dotnet run --project src/PhantomMonitor.App
```

O tema builtin é escolhido com `theme` no `config.json` (`neptune` = paleta derivada do Avalonia, `slate` = paleta derivada do WPF; a chave legada `uiTheme` continua sendo lida e migra), pelo card **INTERFACE** em Configurações (trocado na interface em execução, sem reiniciar) ou pela linha de comando com `--theme=<nome>` (o `--ui-theme=` legado segue aceito).

Algumas leituras de hardware podem exigir permissões elevadas. O FPS usa PresentMon/ETW e pode depender da versão do Windows e das permissões disponíveis.

O pacote completo do renderer dentro do jogo é compilado por
`scripts/build-windows-overlay.ps1`, que gera hooks, launchers e camadas Vulkan
x86/x64. O script exige as ferramentas C++ do Visual Studio 2022 e o Vulkan SDK.
Ainda é necessária validação em execução no Windows para cada API e game capture.

## Releases e atualização automática

Builds publicados ficam na [página de Releases](https://github.com/BadTonho/PhantomMonitor/releases). O app checa novas versões no arranque e a cada 24h e oferece "Atualizar e reiniciar": baixa o pacote da plataforma, troca os arquivos via atualizador one-shot (com backup/rollback) e reabre sozinho — config e logs ficam fora da pasta trocada. Instalação por pacote de sistema (.deb) e pastas protegidas ficam em modo notificação com link. Cada release segue a convenção:

| Asset | Plataforma |
| --- | --- |
| `PhantomMonitor-<versão>-win-x64.zip` | Windows (portável; também é o payload do instalador) |
| `PhantomMonitor-<versão>-linux-x64.tar.gz` | Linux (portável) |
| `PhantomMonitor-<versão>-linux-x64.AppImage` | Linux (AppImage) |
| `PhantomMonitorSetup-<versão>.exe` | Windows (instalador per-user via Inno Setup) |
| `phantom-monitor_<versão>_amd64.deb` | Linux (pacote .deb) |
| `PhantomVulkanLayer-<versão>-linux.so` | Camada Vulkan para FPS/frametime (instalação manual com `install_layer.sh`; não entra na troca automática) |

Releases são geradas localmente por mantenedores (`scripts/release.ps1`, sem CI); detalhes na [documentação de atualizações](docs/funções/atualizações.md) e nos empacotadores em `packaging/`. Os scripts não assinam os binários. O Windows pode exibir o aviso do **SmartScreen** ("editor desconhecido") e o Linux pode pedir confirmação na primeira abertura. O download automático exige o SHA-256 publicado pela API do GitHub; a troca inclui subpastas, conserva backups de recuperação e restaura o conjunto alterado em falhas de cópia tratadas. Feche jogos que usam os hooks Windows antes de atualizar.

## Validação das correções

O tarball Linux usa `tools/ReleasePackager` para gravar permissões POSIX explícitas,
inclusive quando a release é empacotada no Windows.

Execute `dotnet run --project tests/PhantomMonitor.OverlayRegressionTests -c Release`; o runner é um console e precisa ser executado, mesmo estando na solução. `dotnet test` sozinho não o executa. Para C++, use `cmake -S src/PhantomVulkanLayer -B build/native -DBUILD_TESTING=ON`, `cmake --build build/native --config Release` e `ctest --test-dir build/native -C Release --output-on-failure`. No Linux, `sh tests/linux-regression.sh` verifica atualização, rollback e pacote `.deb`.

A camada Vulkan resolve funções dinamicamente e exige apenas os headers do SDK, sem biblioteca de importação de 32 ou 64 bits. O preload OpenGL exige os pacotes de desenvolvimento EGL/GLX; `-DPHANTOM_BUILD_OPENGL=OFF` permite compilar Vulkan e seus testes independentemente. Reinstale a camada Linux para usar o ring v2 com 4.096 quadros. No Windows, o lançamento antecipado está disponível em Configurações para capturar a fila exata de jogos D3D12 com múltiplas filas.

## Configuração e dados

Na primeira execução, o aplicativo cria `config.json` usando [`config.example.json`](config.example.json) como base. Durante o desenvolvimento, a configuração fica na raiz do repositório. Em instalações por usuário, o caminho usual é `%AppData%\PhantomMonitor\config.json` no Windows e `$XDG_CONFIG_HOME/PhantomMonitor/config.json` no Linux.

No Linux, os logs de telemetria e de erros ficam em `~/.local/share/PhantomMonitor/logs/` (ou em `$XDG_DATA_HOME` quando definido). Abra **Diagnóstico** no Avalonia para consultar erros recentes e as fontes de sensores detectadas. A página **Configurações** permite ajustar o intervalo, os formatos de log e opções do overlay. O exportador HTTP vem desativado e restrito a loopback; habilitar acesso a outras interfaces expõe os endpoints sem TLS ou autenticação.

## Arquitetura

```text
src/
├── PhantomMonitor.App/   # App desktop unificado em Avalonia (Windows + Linux)
├── PhantomMonitor.Core/  # Modelos e serviços compartilhados em .NET
├── PhantomVulkanLayer/   # Camada Vulkan nativa para FPS no Linux
└── PhantomSensorLib/     # Biblioteca nativa C++ prevista para extensão futura
```

O Core compartilha modelos, configuração, avaliação de alertas e serviços de telemetria; o app mantém as integrações específicas de cada sistema operacional. A camada Vulkan é compilada separadamente com CMake e `install_layer.sh`; ela não faz parte do build da solução .NET.

## Documentação

A documentação também está disponível em [English](docs/en/README.md); os originais em português estão organizados no [índice pt-BR](docs/README.md).

- **Plataformas:** [Suporte a Linux e camada Vulkan](docs/funções/suporte-linux.md), [FPS e frametime](docs/funções/fps-monitor.md)
- **Interface:** [Overlay](docs/funções/overlay.md), [Inventário de hardware](docs/funções/inventário-hardware.md)
- **Sensores:** [Sensores customizados](docs/funções/sensores-customizados.md), [Ajuste por série](docs/funções/ajuste-de-series.md)
- **Automação e saída:** [Alertas](docs/funções/alertas.md), [Logging](docs/funções/logging.md), [SDK de telemetria](docs/telemetry-sdk.md)
- **Projeto:** [Roadmap de paridade funcional](docs/roadmap-paridade.md), [Arquitetura e módulos](docs/funções/)

## Estado do projeto

O app é o **único frontend desktop Avalonia, tanto no Windows quanto no Linux**; os recursos disponíveis variam por sistema operacional (integrações de sensores, FPS e OSD). Consulte o [roadmap](docs/roadmap-paridade.md) para acompanhar os próximos passos por plataforma.

## Licença

O código original do Phantom Monitor está licenciado sob a GNU Lesser General Public License v3.0 somente (`LGPL-3.0-only`). Consulte [LICENSE](LICENSE). Componentes de terceiros mantêm suas próprias licenças; os avisos do executável PresentMon distribuído com o projeto estão em [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
