# Riva Monitor

Monitor de hardware leve e elegante para Windows. Exibe CPU, GPU, RAM, disco e FPS em tempo real com uma interface WPF moderna e overlay transparente.

## Funcionalidades

- **Monitoramento completo**: CPU (uso, temperatura, clock, potência), GPU (uso, temperatura, clock), RAM (uso, disponível), disco (uso, I/O)
- **Overlay transparente**: Janela flutuante sempre visível sobre jogos com informações em tempo real
- **FPS independente**: Leitura de FPS e frametime de alta precisão via Intel PresentMon (sem RTSS, sem ban de anti-cheat)
- **Gráfico de frametime**: Visualização em tempo real da variação de frametime
- **Logging**: Exportação para CSV e JSON
- **Tema escuro**: Interface moderna com cards e círculos de progresso
- **Cores dinâmicas**: Verde (< 50%), laranja (< 80%), vermelho (≥ 80%) para uso e temperatura
- **Configurável**: Arquivo `config.json` para personalizar sensores, overlay e logging

## Arquitetura

```
RivaShopee/
├── src/
│   ├── RivaMonitor/          # Aplicação WPF em C#
│   │   ├── MainWindow.xaml   # Interface principal
│   │   ├── Overlay/          # Janela overlay transparente
│   │   ├── Models/           # Modelos de dados (SensorData, SensorType)
│   │   ├── Services/         # Monitoramento, logging, configuração
│   │   └── Native/           # P/Invoke para DLL nativa
│   └── RivaSensorLib/        # DLL C++ para sensores (em desenvolvimento)
├── docs/funções/             # Documentação das funcionalidades
└── config.json               # Configuração do aplicativo
```

## Requisitos

- Windows 10 ou superior (build 19041+)
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Visual Studio 2022 (para desenvolvimento)
- Privilégios de administrador (para leitura completa de sensores)

## Como usar

1. Clone o repositório
2. Execute `dotnet run --project src/RivaMonitor`
3. Clique em **"Executar como Admin"** se aparecer o aviso vermelho
4. O monitoramento inicia automaticamente
5. Marque **"Overlay"** na sidebar para ativar a janela sobreposta

## Configuração

Edite `config.json` na raiz do projeto:

```json
{
  "updateIntervalMs": 1000,
  "logToCsv": true,
  "logToJson": true,
  "overlay": {
    "width": 220,
    "showCpu": true,
    "showGpu": true,
    "showRam": true,
    "showDisk": true,
    "showFps": true,
    "accentColor": "#1E90FF"
  }
}
```

## Documentação

Detalhes de cada funcionalidade em `docs/funções/`.

## Tecnologias

- **C# WPF** (.NET 10) — Interface gráfica
- **C++** (MSVC) — DLL nativa para sensores (futuro)
- **LibreHardwareMonitorLib** — Leitura de sensores de hardware
- **Intel PresentMon** — Captura de frames e frametimes via ETW nativo
- **Windows API** — Overlay transparente e detecção de janelas ativas
- **WMI** — Fallback para temperatura e RAM
