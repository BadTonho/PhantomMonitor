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
  <img src="https://img.shields.io/badge/Windows-WPF-0078D4?logo=windows&logoColor=white" alt="Windows com WPF" />
  <img src="https://img.shields.io/badge/Linux-Avalonia-FCC624?logo=linux&logoColor=111111" alt="Linux com Avalonia" />
</p>

<p align="center">
  <a href="#recursos">Recursos</a> ·
  <a href="#comece-a-usar">Comece a usar</a> ·
  <a href="#arquitetura">Arquitetura</a> ·
  <a href="#documentacao">Documentação</a>
</p>

O **Phantom Monitor** acompanha sensores do computador e apresenta as leituras em tempo real. Ele reúne uso, temperaturas, clocks, memória, armazenamento, rede, ventoinhas e telemetria de jogos em uma interface desktop.

O projeto tem dois frontends: **WPF no Windows** e **Avalonia no Linux**. Cada plataforma oferece recursos próprios; a tabela abaixo resume as diferenças atuais.

## Recursos

| Recurso | Windows · WPF | Linux · Avalonia |
| --- | --- | --- |
| Sensores | CPU, GPU, RAM, discos, rede, ventoinhas e sensores da placa-mãe | Sensores de `sysfs`, `procfs`, `hwmon`, DRM e NVML da NVIDIA quando a biblioteca do driver está instalada |
| FPS e frametime | PresentMon via ETW | Camada Vulkan Phantom para jogos Vulkan |
| Overlay | OSD avançado, click-through, perfis e editor visual | Janela flutuante configurável; o compositor controla a posição e o fullscreen |
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

Inicie o frontend WPF:

```powershell
dotnet run --project src/PhantomMonitor
```

Algumas leituras de hardware podem exigir permissões elevadas. O FPS usa PresentMon/ETW e pode depender da versão do Windows e das permissões disponíveis.

## Configuração e dados

Na primeira execução, o aplicativo cria `config.json` usando [`config.example.json`](config.example.json) como base. Durante o desenvolvimento, a configuração fica na raiz do repositório. Em instalações por usuário, o caminho usual é `%AppData%\PhantomMonitor\config.json` no Windows e `$XDG_CONFIG_HOME/PhantomMonitor/config.json` no Linux.

No Linux, os logs de telemetria e de erros ficam em `~/.local/share/PhantomMonitor/logs/` (ou em `$XDG_DATA_HOME` quando definido). Abra **Diagnóstico** no Avalonia para consultar erros recentes e as fontes de sensores detectadas. A página **Configurações** permite ajustar o intervalo, os formatos de log e opções do overlay. O exportador HTTP vem desativado e restrito a loopback; habilitar acesso a outras interfaces expõe os endpoints sem TLS ou autenticação.

## Arquitetura

```text
src/
├── PhantomMonitor.App/   # Frontend Avalonia para Linux
├── PhantomMonitor.Core/  # Modelos e serviços compartilhados em .NET
├── PhantomMonitor/       # Frontend WPF para Windows
├── PhantomVulkanLayer/   # Camada Vulkan nativa para FPS no Linux
└── PhantomSensorLib/     # Biblioteca nativa C++ prevista para extensão futura
```

O Core compartilha modelos, configuração, avaliação de alertas e serviços de telemetria. Os frontends mantêm as integrações específicas de cada sistema operacional. A camada Vulkan é compilada separadamente com CMake e `install_layer.sh`; ela não faz parte do build da solução .NET.

## Documentação

A documentação também está disponível em [English](docs/en/README.md); os originais em português estão organizados no [índice pt-BR](docs/README.md).

- **Plataformas:** [Suporte a Linux e camada Vulkan](docs/funções/suporte-linux.md), [FPS e frametime](docs/funções/fps-monitor.md)
- **Interface:** [Overlay](docs/funções/overlay.md), [Inventário de hardware](docs/funções/inventário-hardware.md)
- **Sensores:** [Sensores customizados](docs/funções/sensores-customizados.md), [Ajuste por série](docs/funções/ajuste-de-series.md)
- **Automação e saída:** [Alertas](docs/funções/alertas.md), [Logging](docs/funções/logging.md), [SDK de telemetria](docs/telemetry-sdk.md)
- **Projeto:** [Roadmap de paridade funcional](docs/roadmap-paridade.md), [Arquitetura e módulos](docs/funções/)

## Estado do projeto

O frontend Avalonia é a interface Linux atual. No Windows, o frontend WPF continua sendo a aplicação principal; os recursos de interface e as ações disponíveis podem variar entre plataformas. Consulte o [roadmap](docs/roadmap-paridade.md) para acompanhar a migração.

## Licença

O código original do Phantom Monitor está licenciado sob a GNU Lesser General Public License v3.0 somente (`LGPL-3.0-only`). Consulte [LICENSE](LICENSE). Componentes de terceiros mantêm suas próprias licenças; os avisos do executável PresentMon distribuído com o projeto estão em [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
