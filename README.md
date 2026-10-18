# Phantom Monitor

Monitor de hardware leve, elegante e de alta precisão com suporte multiplataforma (**Windows e Linux**). Exibe CPU, GPU, RAM, disco e FPS em tempo real com uma interface moderna e limpa, editor visual de overlay estilo RTSS, e **sem dependência de softwares de terceiros** (sem MSI Afterburner/RTSS no Windows, sem MangoHud no Linux).

## Funcionalidades

- **Monitoramento completo**: CPU (uso, temperatura, clock, potência, voltagem), GPU (uso, temperatura, VRAM usada/total, clock, potência, hotspot), RAM (uso, disponível), disco (uso, I/O), rede (↓/↑), ventoinhas (GPU/sistema) e temperaturas de placa-mãe/SuperIO
- **Overlay configurável & transparente**: Janela flutuante sempre visível sobre jogos com informações em tempo real, sublinhas de detalhes (CPU/GPU) e linhas opcionais NET/FAN/MB com auto-revelação, além do Editor Visual (grid canvas, drag-and-drop, modo compacto, cor de destaque e tamanho de fonte)
- **FPS 100% independente de terceiros**:
  - **Windows**: Leitura de FPS e frametime de alta precisão via Intel PresentMon (ETW nativo, sem injetar DLLs, imune a banimentos de anti-cheat)
  - **Linux**: Camada nativa em C++ `PhantomVulkanLayer` interceptando `vkQueuePresentKHR` com comunicação por memória compartilhada (`/dev/shm/phantom_fps_<pid>`, um objeto por processo)
- **Atalho global do OSD**: Mostrar/ocultar o overlay de qualquer aplicativo via Win32 `RegisterHotKey` (ex.: `Ctrl+Shift+O`), sem injeção de DLL
- **Bandeja do sistema**: Ícone na bandeja (Win32 puro, sem WinForms) com menu de contexto — mostrar janela, alternar OSD, iniciar com o Windows e sair — e opção de autostart via registro do usuário
- **Gráfico de frametime**: Visualização em tempo real da variação de frametime com buffer circular
- **Estatísticas avançadas**: FPS instantâneo, médio, mínimo, máximo, 1% Low e 0.1% Low
- **Alertas com ações**: Regras por sensor (acima/abaixo de um limiar) com popup, som, script externo e desligamento agendado (com trava de segurança `allowShutdown`)
- **Logging**: Exportação contínua para CSV e JSON
- **Visual Clean**: Design profissional refinado (paleta Slate Dark), eliminando efeitos excessivos de neon
- **Configurável**: `config.json` com fallback automático para `config.example.json`

## Arquitetura Multiplataforma

```
Phantom/
├── src/
│   ├── PhantomMonitor/           # Aplicação desktop (.NET 10)
│   │   ├── MainWindow.xaml       # Interface principal clean
│   │   ├── Overlay/              # Overlay transparente e Editor Visual (OverlayEditorWindow)
│   │   ├── Models/               # Modelos de dados (SensorData, SensorType)
│   │   └── Services/             # Provedores de telemetria desacoplados
│   │       ├── ISensorService.cs # Contrato de hardware
│   │       ├── IFpsMonitor.cs    # Contrato de taxas de quadro
│   │       ├── SensorServiceFactory.cs # Seleção de provedor por SO
│   │       ├── HardwareMonitorService.cs # Windows (LibreHardwareMonitor + PresentMon)
│   │       └── Linux/            # Linux nativo (procfs, sysfs, hwmon, drm)
│   │           ├── LinuxSensorService.cs
│   │           └── LinuxFpsMonitor.cs
│   ├── PhantomVulkanLayer/       # Camada Vulkan nativa em C++ para Linux
│   │   ├── PhantomVulkanLayer.cpp   # Intercepta vkQueuePresentKHR e grava em /dev/shm
│   │   ├── PhantomVulkanTelemetry.h # Contrato binário de memória compartilhada
│   │   ├── phantom_vulkan_layer.json# Manifesto oficial Vulkan
│   │   └── install_layer.sh         # Script de compilação e instalação da camada
│   └── PhantomSensorLib/         # Biblioteca nativa C++ para sensores
├── docs/funções/                 # Documentação detalhada de cada módulo
├── config.example.json           # Template de configuração versionado
└── AGENTS.md                     # Diretrizes e regras do repositório
```

## Requisitos

### Windows:
- Windows 10 ou superior (build 19041+)
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Privilégios de administrador (para leitura completa dos sensores de hardware)

### Linux (camada Vulkan — sem front-end):
- Distribuição Linux com Kernel 5.4+
- CMake e compilador C++ (GCC/Clang) para construir a camada
- Drivers Vulkan instalados (Mesa RADV/ANV ou driver NVIDIA proprietário)
- Permissões padrão para leitura de `/sys/class/hwmon` e `/dev/shm`

## Como usar

### No Windows:
1. Clone o repositório
2. Execute `dotnet run --project src/PhantomMonitor`
3. Clique em **"Executar como Admin"** se solicitado
4. Ative o **"Overlay"** na sidebar ou abra o **"Editar Layout OSD"** para customizar o posicionamento

### Linux (apenas a camada Vulkan — ainda não há front-end):
1. Compile e instale a camada:
   ```bash
   cd src/PhantomVulkanLayer
   chmod +x install_layer.sh
   ./install_layer.sh
   ```
2. Inicie seu jogo com a camada ativada (ex: Opções de Inicialização na Steam):
   ```bash
   ENABLE_PHANTOM_OVERLAY=1 %command%
   ```

> [!IMPORTANT]
> O app WPF (`PhantomMonitor`) executa **somente no Windows** (`net10.0-windows`) — `dotnet run` não funciona no Linux. No Linux, a camada Vulkan é independente (publica os frametimes em `/dev/shm/phantom_fps_<pid>`); um leitor/monitor Linux é um front-end futuro (ver [suporte-linux.md](docs/funções/suporte-linux.md)).

## Configuração

O `config.json` é criado automaticamente a partir de `config.example.json` na primeira execução:

- **Desenvolvimento** (executando dentro do repositório): `config.json` na raiz do repositório (detectada pelo `PhantomMonitor.slnx`).
- **Publicado/instalado**: `%AppData%\PhantomMonitor\config.json`.

Exemplo (esquema real, veja [configuracao.md](docs/funções/configuracao.md)):

```json
{
  "updateIntervalMs": 1000,
  "logToCsv": false,
  "logToJson": true,
  "showOverlay": true,
  "overlayOpacity": 0.85,
  "overlayPosition": { "x": 20, "y": 80 },
  "overlay": {
    "width": 220,
    "showCpu": true,
    "showGpu": true,
    "showRam": true,
    "showDisk": true,
    "showFps": true,
    "showFrametimeGraph": true
  }
}
```

## Build e notas

- O app WPF (`net10.0-windows`) executa **somente no Windows**. O suporte a Linux (`Services/Linux/` + `PhantomVulkanLayer`) é a base para um futuro front-end multiplataforma (ver [suporte-linux.md](docs/funções/suporte-linux.md)).
- Os fontes de `PhantomSensorLib` (C++, stub) ficam no repositório como extensão futura, **sem pipeline de build ativo** — nenhum código consome a DLL hoje (ver [sensor-nativo.md](docs/funções/sensor-nativo.md)).
- O FPS no Windows requer **Administrador** (PresentMon/ETW). Erros internos ficam em `logs/errors.log`.
- O `PhantomVulkanLayer` é compilado via CMake/`install_layer.sh` e não faz parte do `PhantomMonitor.slnx`.

## Documentação

Consulte os guias específicos em [docs/funções/](docs/funções/):
- [Suporte a Linux e Camada Vulkan](docs/funções/suporte-linux.md)
- [Monitoramento de FPS e Frametime](docs/funções/fps-monitor.md)
- [Editor Visual de Overlay](docs/funções/overlay.md)
- [Atalho Global do OSD](docs/funções/hotkey.md)
- [Bandeja do Sistema e Autostart](docs/funções/tray.md)
- [Alertas com Ações](docs/funções/alertas.md)
- [Hardware Monitor Service](docs/funções/hardware-monitor.md)
- [Configuração](docs/funções/configuracao.md)
- [Logging e ErrorLog](docs/funções/logging.md)
- [Biblioteca nativa (PhantomSensorLib)](docs/funções/sensor-nativo.md)
- [Formatação de sensores (SensorFormatter)](docs/funções/sensor-formatter.md)
- [Inventário de Hardware](docs/funções/inventário-hardware.md)
- [Roadmap de paridade (RTSS/Afterburner · HWiNFO)](docs/roadmap-paridade.md)
