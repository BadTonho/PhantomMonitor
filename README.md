# Riva Monitor

Monitor de hardware leve, elegante e de alta precisão com suporte multiplataforma (**Windows e Linux**). Exibe CPU, GPU, RAM, disco e FPS em tempo real com uma interface moderna e limpa, editor visual de overlay estilo RTSS, e **sem dependência de softwares de terceiros** (sem MSI Afterburner/RTSS no Windows, sem MangoHud no Linux).

## Funcionalidades

- **Monitoramento completo**: CPU (uso, temperatura, clock, potência), GPU (uso, temperatura, VRAM, clock), RAM (uso, disponível), disco (uso, I/O)
- **Overlay configurável & transparente**: Janela flutuante sempre visível sobre jogos com informações em tempo real e Editor Visual de posições (grid canvas, drag-and-drop, tamanho e espaçamento)
- **FPS 100% independente de terceiros**:
  - **Windows**: Leitura de FPS e frametime de alta precisão via Intel PresentMon (ETW nativo, sem injetar DLLs, imune a banimentos de anti-cheat)
  - **Linux**: Camada nativa em C++ `RivaVulkanLayer` interceptando `vkQueuePresentKHR` com comunicação por memória compartilhada (`/dev/shm/riva_fps`)
- **Gráfico de frametime**: Visualização em tempo real da variação de frametime com buffer circular
- **Estatísticas avançadas**: FPS instantâneo, FPS médio, mínimo e 1% Low
- **Logging**: Exportação contínua para CSV e JSON
- **Visual Clean**: Design profissional refinado (paleta Slate Dark), eliminando efeitos excessivos de neon
- **Configurável**: `config.json` com fallback automático para `config.example.json`

## Arquitetura Multiplataforma

```
RivaShopee/
├── src/
│   ├── RivaMonitor/              # Aplicação desktop (.NET 10)
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
│   ├── RivaVulkanLayer/          # Camada Vulkan nativa em C++ para Linux
│   │   ├── RivaVulkanLayer.cpp   # Intercepta vkQueuePresentKHR e grava em /dev/shm
│   │   ├── RivaVulkanTelemetry.h # Contrato binário de memória compartilhada
│   │   ├── riva_vulkan_layer.json# Manifesto oficial Vulkan
│   │   └── install_layer.sh      # Script de compilação e instalação da camada
│   └── RivaSensorLib/            # Biblioteca nativa C++ para sensores
├── docs/funções/                 # Documentação detalhada de cada módulo
├── config.example.json           # Template de configuração versionado
└── AGENTS.md                     # Diretrizes e regras do repositório
```

## Requisitos

### Windows:
- Windows 10 ou superior (build 19041+)
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Privilégios de administrador (para leitura completa dos sensores de hardware)

### Linux:
- Distribuição Linux com Kernel 5.4+
- .NET 10 SDK (Runtime desktop / CLI)
- Vulkan SDK e drivers Vulkan instalados (Mesa RADV/ANV ou driver NVIDIA proprietário)
- Permissões padrão para leitura de `/sys/class/hwmon` e `/dev/shm`

## Como usar

### No Windows:
1. Clone o repositório
2. Execute `dotnet run --project src/RivaMonitor`
3. Clique em **"Executar como Admin"** se solicitado
4. Ative o **"Overlay"** na sidebar ou abra o **"Editar Layout OSD"** para customizar o posicionamento

### No Linux:
1. Compile e instale a camada Vulkan:
   ```bash
   cd src/RivaVulkanLayer
   chmod +x install_layer.sh
   ./install_layer.sh
   ```
2. Inicie o RivaMonitor:
   ```bash
   dotnet run --project src/RivaMonitor
   ```
3. Inicie seu jogo com a camada ativada (ex: Opções de Inicialização na Steam):
   ```bash
   ENABLE_RIVA_OVERLAY=1 %command%
   ```

## Configuração

Copie `config.example.json` para `config.json` e personalize conforme desejado:

```json
{
  "updateIntervalMs": 1000,
  "logToCsv": false,
  "logToJson": false,
  "overlay": {
    "x": 20,
    "y": 20,
    "showCpu": true,
    "showGpu": true,
    "showRam": true,
    "showDisk": true,
    "showFps": true,
    "fontSize": 14,
    "lineSpacing": 4,
    "backgroundColor": "#D914171E",
    "accentColor": "#3B82F6",
    "textColor": "#F1F5F9"
  }
}
```

## Documentação

Consulte os guias específicos em [docs/funções/](file:///docs/funções/):
- [Suporte a Linux e Camada Vulkan](file:///docs/funções/suporte-linux.md)
- [Monitoramento de FPS e Frametime](file:///docs/funções/fps-monitor.md)
- [Editor Visual de Overlay](file:///docs/funções/overlay.md)
- [Hardware Monitor Service](file:///docs/funções/hardware-monitor.md)
