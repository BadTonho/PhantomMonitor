# Phantom Monitor

Monitor de hardware leve, elegante e de alta precisão com suporte multiplataforma (**Windows e Linux**). Exibe CPU, GPU, RAM, disco e FPS em tempo real com uma interface moderna e limpa, editor visual de overlay dedicado, e **sem dependência de softwares de monitoramento de terceiros** (sem instrumentação/overlays externos no Windows, sem MangoHud no Linux).

## Funcionalidades

- **Monitoramento completo**: CPU (uso, temperatura, clock, potência, voltagem), GPU (uso, temperatura, VRAM usada/total, **temperatura da VRAM**, clock, potência, hotspot), RAM (uso, disponível), disco (uso, I/O, temperatura, saúde e totais SMART), rede (↓/↑ e totais da sessão), ventoinhas (GPU/sistema), temperaturas de placa-mãe/SuperIO (**rails de tensão, VRM e módulos de RAM**) e **bateria** (carga, tensão, potência, desgaste e fonte AC — laptops/UPs; Windows via WMI, Linux via `/sys/class/power_supply`) + **identidade dos monitores** (fabricante/modelo/série/diagonal do EDID via WMI, no inventário e no snapshot JSON)
- **Overlay configurável**: O WPF oferece o OSD avançado e seu Editor Visual; o Avalonia/Linux inclui janela flutuante com categorias, séries por instância, largura, opacidade, fonte e modo compacto ajustáveis, além de gráfico de histórico de frametime configurável
- **Linhas personalizadas no OSD (templates)**: texto livre com placeholders `{Chave}`/`{Chave:N}`/wildcard resolvidos a cada ciclo; linha invisível até todos os sensores existirem (`overlay.templates`)
- **Sensores customizados (fórmulas)**: o Avalonia oferece editor visual para criar, remover e reordenar séries derivadas (`Custom#i`), com validação e aplicação ao salvar; fórmulas usam operadores `+ - * /`, funções `min/max/abs` e referências por chave/wildcard/nome, e entram nas estatísticas, CSV/JSON, SDK e alertas como séries nativas (`customSensors`)
- **Cadência e suavização por série**: polling por dono da série (`sensorTuning`: `intervalMs` por regra — entre amostras a série repete o último valor, estatísticas só contam amostras frescas) + filtro de média móvel (`smoothing` até 60 amostras) para matar jitter de temperatura/uso sem amansar as médias
- **Editor de ajuste por série**: no Avalonia, escolha séries do inventário ou digite padrões, configure intervalo e suavização, reordene regras sobrepostas e aplique sem editar o `config.json`
- **Gráficos de histórico por série no OSD**: sparklines configuráveis (`overlay.graphs`) para qualquer série — resolução por chave/wildcard/nome, janela circular própria (30–1200 amostras), eixo auto ou fixo, cor por gráfico; renderizados logo antes das linhas de template
- **Perfis por aplicativo**: overrides de posição/largura/fonte/acento/séries por app — e `visible: false` para esconder o OSD completo em jogos específicos
- **FPS 100% independente de terceiros**:
  - **Windows**: Leitura de FPS e frametime de alta precisão via Intel PresentMon (ETW nativo, sem injetar DLLs, imune a banimentos de anti-cheat) — com **GPU Busy / CPU Busy / CPU Wait / latência de present** quando a versão do executável expõe as colunas
  - **Linux**: Camada nativa em C++ `PhantomVulkanLayer` interceptando `vkQueuePresentKHR` com comunicação por memória compartilhada (`/dev/shm/phantom_fps_<pid>`, um objeto por processo)
- **Atalhos globais**: mostrar/ocultar o OSD (`toggleOverlayHotkey`) e **pausar/retomar o monitoramento** (`pauseMonitorHotkey`, padrão `Ctrl+Alt+P`) via Win32 `RegisterHotKey`, sem injeção de DLL
- **Bandeja do sistema**: Ícone na bandeja (Win32 puro, sem WinForms) com menu de contexto — mostrar janela, alternar OSD, iniciar com o Windows, **resetar estatísticas**, **pausar monitoramento** e sair — e opção de autostart via registro do usuário
- **Gráfico de frametime**: Visualização em tempo real da variação de frametime com buffer circular
- **Estatísticas avançadas de FPS**: instantâneo, médio, mínimo, máximo, 1% Low, 0.1% Low e **percentis puros 1st/99th de FPS e frametime**
- **Erros de hardware WHEA**: série `WheaErrorCount` (Windows) contando eventos do provedor `Microsoft-Windows-WHEA-Logger` desde o início da sessão — combinável com alertas e exportações
- **Estatísticas por série**: mín/máx/média da sessão para **qualquer série** (canônica e por instância), com reset pela bandeja/inventário e ranking na janela de inventário
- **Alertas por sensor**: Regras acima/abaixo de um limite com cooldown e seleção por instância; o WPF oferece popup, som, scripts e desligamento protegido por `allowShutdown`, enquanto o Avalonia oferece toast interno, histórico, notificações nativas em segundo plano e execução de scripts Linux (`.sh` via `/bin/sh` ou arquivos executáveis)
- **Logging**: Exportação contínua para CSV e JSON — CSV aceita **marcadores `#marker`** para delimitar segmentos de sessão/benchmark; **journal JSON contínuo (JSONL)** opcional com uma linha por amostra e notas automáticas (início de sessão/reset) (`logToJsonJournal`)
- **SDK de telemetria**: memória compartilhada sempre ativa + **exportador HTTP opt-in** (`telemetryExport`) — consuma o snapshot em JSON (mesmo payload do `sensors_latest.json`) via `GET /values` no loopback ou LAN (ver `docs/telemetry-sdk.md`)
- **Visual Clean**: Design profissional refinado (paleta Slate Dark), eliminando efeitos excessivos de neon
- **Inventário no Linux**: Página no dashboard Avalonia com séries publicadas por CPU, GPU, RAM, disco, rede, ventoinhas, bateria e FPS; tipos Linux esperados sem leitura aparecem como indisponíveis
- **Configurável**: `config.json` com fallback automático para `config.example.json`

## Arquitetura Multiplataforma

```
Phantom/
├── src/
│   ├── PhantomMonitor.App/       # Frontend Avalonia Linux (.NET 10)
│   │   ├── Views/                # Janela principal
│   │   ├── ViewModels/           # Estado do dashboard e controles
│   │   └── Controls/             # Gráficos de histórico
│   ├── PhantomMonitor.Core/      # Modelos e serviços compartilhados (net10.0)
│   │   ├── Models/               # SensorData, SensorType, SensorKey e config
│   │   └── Services/Linux/       # procfs, sysfs, hwmon, DRM e FPS Vulkan
│   ├── PhantomMonitor/           # Frontend WPF atual para Windows (transição)
│   │   ├── MainWindow.xaml       # Interface principal clean
│   │   ├── Overlay/              # Overlay transparente e Editor Visual (OverlayEditorWindow)
│   │   └── Services/             # Integrações de hardware e desktop para Windows
│   ├── PhantomVulkanLayer/       # Camada Vulkan nativa em C++ para Linux
│   │   ├── PhantomVulkanLayer.cpp   # Intercepta vkQueuePresentKHR e grava em /dev/shm
│   │   ├── PhantomVulkanTelemetry.h # Contrato binário de memória compartilhada
│   │   ├── phantom_vulkan_layer.json# Manifesto oficial Vulkan
│   │   └── install_layer.sh         # Script de compilação e instalação da camada
│   └── PhantomSensorLib/         # Biblioteca nativa C++ para sensores
├── tools/                        # PresentMon.exe (ETW) e leitor de exemplo da SDK (TelemetrySdkReader)
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
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Avalonia é restaurado pelo NuGet no primeiro build
- Distribuição Linux com Kernel 5.4+
- (Opcional, para FPS em jogos) CMake e compilador C++ (GCC/Clang) para construir a camada
- Drivers Vulkan instalados (Mesa RADV/ANV ou driver NVIDIA proprietário)
- Permissões padrão para leitura de `/sys/class/hwmon` e `/dev/shm`

## Como usar

### No Windows:
1. Clone o repositório
2. Execute `dotnet run --project src/PhantomMonitor`
3. Clique em **"Executar como Admin"** se solicitado
4. Ative o **"Overlay no Jogo"** na sidebar ou abra o **"⚙ Configurar Overlay..."** para customizar o posicionamento

### No Linux (frontend Avalonia):
1. (Opcional, para FPS em jogos) Compile e instale a camada Vulkan:
   ```bash
   cd src/PhantomVulkanLayer
   chmod +x install_layer.sh
   ./install_layer.sh
   ```
2. Inicie o dashboard:
   ```bash
   dotnet run --project src/PhantomMonitor.App
   ```
3. Abra **Inventário** na barra lateral para consultar os valores recebidos e os tipos de sensor sem leitura disponível.
4. Use **Ajuste por série** para editar as regras `sensorTuning`: selecione séries do inventário ou digite padrões como `Gpu*`, ajuste intervalo/suavização e reordene regras sobrepostas.
5. Para coletar FPS, inicie um jogo Vulkan com a camada ativada (ex.: opções de inicialização da Steam):
   ```bash
   ENABLE_PHANTOM_OVERLAY=1 %command%
   ```
6. Use **Configurações → Exportador HTTP** para ativar `GET /values` e `GET /health`, escolher a porta e, opcionalmente, permitir acesso pela rede local. O padrão é desativado e restrito ao próprio computador.
7. A gravação é controlada pelas opções `logToCsv`, `logToJson` e `logToJsonJournal` do `config.json`. No Linux, os arquivos ficam em `~/.local/share/PhantomMonitor/logs/` (ou no diretório definido por `XDG_DATA_HOME`).
8. Use **Configurações** na barra lateral para alterar o intervalo global de coleta e os formatos de gravação pela interface.
9. Use **Mostrar/Ocultar overlay** na visão geral; escolha as categorias e configure séries por instância detectadas ou padrões manuais em **Configurações → Overlay flutuante**.

> [!IMPORTANT]
> O dashboard Avalonia é, nesta primeira versão, o frontend Linux. O WPF permanece como o frontend Windows durante a migração; a coleta Windows ainda não foi ligada ao app Avalonia. No Linux, a UI e o overlay mostram FPS quando um jogo Vulkan habilita a camada Phantom. O overlay Linux é uma janela desktop arrastável e interativa; o compositor controla sua posição inicial e se ela aparece sobre aplicativos em tela cheia.

## Configuração

O `config.json` é criado automaticamente a partir de `config.example.json` na primeira execução:

- **Desenvolvimento** (executando dentro do repositório): `config.json` na raiz do repositório (detectada pelo `PhantomMonitor.slnx`).
- **Publicado/instalado**: arquivo `config.json` na pasta de dados da aplicação do usuário (AppData no Windows; configuração do usuário no Linux).

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

- `PhantomMonitor.App` usa Avalonia e `PhantomMonitor.Core` (`net10.0`) para compartilhar modelos e serviços; `PhantomMonitor` (WPF) continua sendo o frontend Windows durante a transição.
- O dashboard Avalonia inclui visão geral, inventário, editores de sensores customizados e ajuste por série, configurações de coleta/logs e overlay Linux, exportador HTTP, além de edição de alertas com notificações internas e scripts Linux. Bandeja, som e desligamento continuam disponíveis somente no WPF; o WPF mantém seu overlay avançado e editor visual.
- Os fontes de `PhantomSensorLib` (C++, stub) ficam no repositório como extensão futura, **sem pipeline de build ativo** — nenhum código consome a DLL hoje (ver [sensor-nativo.md](docs/funções/sensor-nativo.md)).
- O FPS no Windows requer **Administrador** (PresentMon/ETW). Erros internos ficam em `logs/errors.log`.
- O `PhantomVulkanLayer` é compilado via CMake/`install_layer.sh` e não faz parte do `PhantomMonitor.slnx`.

### Políticas por design (anti-cheat)

O monitor **não injeta DLLs nem faz hooks no processo dos jogos** — no Windows, seu OSD é uma
janela própria (WS_EX_LAYERED), o FPS vem do ETW via PresentMon e a telemetria é publicada por
memória compartilhada. É por isso que o projeto **não implementa** um OSD in-frame via hook
no processo do jogo: hooks de `Present`/DXGI são a assinatura clássica rastreada por EAC/BattlEye/Vanguard
e podem derrubar contas. O estudo completo do caminho seguro (plugin OBS consumindo a SDK de
telemetria) está em [docs/osd-in-frame-estudo.md](docs/osd-in-frame-estudo.md).

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
- [Sensores customizados (fórmulas)](docs/funções/sensores-customizados.md)
- [Cadência de amostragem e suavização por série (P9.1)](docs/funções/ajuste-de-series.md)
- [Inventário de Hardware](docs/funções/inventário-hardware.md)
- [Interface principal (MainWindow)](docs/funções/interface-principal.md)
- [Modelos de dados (SensorType/SensorKey)](docs/funções/modelos-dados.md)
- [Buffer de frametime (FrametimeBuffer)](docs/funções/frametime-buffer.md)
- [SDK de Telemetria (memória compartilhada)](docs/telemetry-sdk.md)
- [Estudo: OSD in-frame no Windows (P4)](docs/osd-in-frame-estudo.md)
- [Roadmap de paridade funcional](docs/roadmap-paridade.md)
