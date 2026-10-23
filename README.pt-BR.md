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
| Sensores | CPU, GPU, RAM, discos, rede, ventoinhas e sensores da placa-mãe | Sensores de `sysfs`, `procfs`, `hwmon`, DRM e NVML da NVIDIA quando a biblioteca do driver está instalada |
| FPS e frametime | PresentMon via ETW | Camada Vulkan Phantom para jogos Vulkan |
| Overlay | OSD avançado, click-through e perfis (edição via config.json) | Janela flutuante configurável; o compositor controla a posição e o fullscreen |
| Inventário | Inventário de hardware e estatísticas por série | Séries detectadas agrupadas por categoria; sensores sem leitura aparecem como indisponíveis |
| Alertas | Notificação, som, scripts e desligamento configurável | Toast interno, notificações do desktop e execução de scripts |
| Diagnóstico | Fontes de sensores em tempo real e log de erros | Estado das fontes, erros recentes e detalhes do driver/NVML/hwmon NVIDIA |
| Fórmulas e ajuste por série | Compatibilidade com as configurações existentes | Editores visuais de sensores customizados e ajuste de cadência/suavização |
| Telemetria | CSV, JSON, JSONL e exportador HTTP opcional | CSV, JSON, JSONL e exportador HTTP opcional |

Os recursos que dependem do hardware ou do compositor podem variar entre máquinas. No Linux, o FPS requer um jogo Vulkan e a camada Phantom ativada. No Windows, a coleta de FPS usa ETW; o monitor não injeta DLLs nos jogos.

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

O dashboard e o overlay Linux funcionam sem a camada quando você não precisa de FPS. Em Wayland, o compositor decide a posição inicial do overlay e sua presença sobre aplicativos em fullscreen.

### Windows

Inicie o app:

```powershell
dotnet run --project src/PhantomMonitor.App
```

O tema builtin é escolhido com `uiTheme` no `config.json` (`neptune` = visual original do Avalonia, `slate` = porta verbatim do visual do WPF) ou pela linha de comando com `--ui-theme=<nome>`.

Algumas leituras de hardware podem exigir permissões elevadas. O FPS usa PresentMon/ETW e pode depender da versão do Windows e das permissões disponíveis.

## Releases e atualização automática

Builds publicados ficam na [página de Releases](https://github.com/BadTonho/PhantomMonitor/releases). O app checa novas versões no arranque e a cada 24h e oferece "Atualizar e reiniciar": baixa o pacote da plataforma, troca os arquivos via atualizador one-shot (com backup/rollback) e reabre sozinho — config e logs ficam fora da pasta trocada. Instalação por pacote de sistema (.deb) e pastas protegidas ficam em modo notificação com link. Cada release segue a convenção:

| Asset | Plataforma |
| --- | --- |
| `PhantomMonitor-<versão>-win-x64.zip` | Windows (portável; também é o payload do instalador) |
| `PhantomMonitor-<versão>-linux-x64.tar.gz` | Linux (portável) |
| `PhantomMonitor-<versão>-linux-x64.AppImage` | Linux (AppImage) |
| `PhantomMonitorSetup-<versão>.exe` | Windows (instalador per-user via Inno Setup) |
| `phantom-monitor_<versão>_amd64.deb` | Linux (pacote .deb) |
| `PhantomVulkanLayer-<versão>-linux.so` | Camada Vulkan (instalação manual com `install_layer.sh`; não entra na troca automática) |

Releases são geradas localmente por mantenedores (`scripts/release.ps1`, sem CI); detalhes na [documentação de atualizações](docs/funções/atualizações.md) e nos empacotadores em `packaging/`. Como os pacotes são binários independentes assinados por nós, o Windows pode exibir o aviso do **SmartScreen** ("editor desconhecido") e o Linux pode pedir confirmação de execução na primeira abertura.

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
