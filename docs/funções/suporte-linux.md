# Suporte Nativo a Linux (Multiplataforma)

> **Idioma:** Português (Brasil) · [English](../en/functions/linux-support.md)

O **Phantom Monitor** oferece suporte arquitetural e operacional para Linux mantendo rigorosamente a premissa fundamental do projeto: **independência total de softwares ou utilitários de terceiros** (sem depender de MangoHud, GOverlay ou drivers proprietários adicionais).

> [!IMPORTANT]
> **Escopo atual:** o app único (`src/PhantomMonitor.App`) é a interface desktop do projeto e, no Linux, consome o `LinuxSensorService` de `PhantomMonitor.Core`. Ele cobre monitoramento ao vivo, FPS, inventário completo (séries publicadas com estatísticas de sessão, árvore de hardware quando o coletor publica as linhas `[HWR]` — hoje o Windows — e card de monitores, populado pelo WMI no Windows; no Linux a lista fica vazia e o card oculto), editores de sensores customizados e ajuste por série, gravação CSV/JSON/JSONL, configurações de coleta/logs e do overlay flutuante, exportação HTTP opcional (`/values` e `/health`), inicialização com o sistema por entrada XDG autostart (`~/.config/autostart/phantom-monitor.desktop`) e edição de alertas com toasts internos e notificações nativas via D-Bus quando a janela fica em segundo plano. O overlay Linux respeita `showOverlay` e as categorias existentes em `overlay`, é arrastável/interativo e acompanha cada snapshot; transparência pode depender do compositor e janelas em tela cheia são controladas pelo desktop. O exportador é configurado pela página Configurações e escuta somente durante a coleta, mantendo o último snapshot na pausa; por padrão fica desativado e restrito ao loopback. O ajuste por série edita o bloco `sensorTuning`, com alvos exatos ou padrões, intervalo, suavização e ordem de precedência. As regras de alerta são salvas no bloco `alerts`; scripts podem ser iniciados em disparos (`.sh` por `/bin/sh`; outros arquivos precisam ser executáveis), enquanto no Linux o som fica por conta da notificação de desktop e o desligamento não é executado (no Windows o app unificado executa som, script e desligamento com guard `allowShutdown`, paridade do WPF, desde a F1.5). O frontend WPF foi aposentado na Fase 2 da unificação visual (`6c4d2c0`) — o app único é a interface nos dois sistemas; suas ações de alerta e overlay avançado vivem no app unificado. Desde a F1.1, o app Avalonia no Windows lê os mesmos sensores pela fábrica multiplataforma `SensorServiceFactory` (LibreHardwareMonitor + PresentMon via `Core/Services/Sensors/Windows/`), e desde a F1.2 seu dashboard tem pílulas de temperatura com cor por faixa, linhas de detalhe de CPU/GPU, o card Placa-mãe & ventoinhas, a lista de processos ativos na GPU (relatados pelo Windows via WMI GPUEngine), o badge de atualização no box ESTADO e o banner de elevação com "Executar como admin" (UAC), e desde a F1.3 o overlay no Windows ganhou o OSD completo (perfis por aplicativo, gráficos por série, templates de texto e clique-through real com posição pelo app em foco e Topmost reforçado), enquanto no Linux o overlay segue arrastável/interativo com Topmost fixo — sem follow, clique-through ou matcher de perfis por app. Desde a F1.5 o app unificado também tem bandeja própria com o menu completo (mostrar, overlay, iniciar com o sistema, reset de estatísticas, pausa, pin de perfil do OSD e sair) — no Linux via StatusNotifier/DBus, em ambientes sem suporte o ícone simplesmente não aparece e o app segue funcional —, além de inicialização com o sistema por entrada XDG autostart (`~/.config/autostart/phantom-monitor.desktop`), instância única por lock de arquivo (`logs/session.lock`) e applier de atualização por SO (tarball/AppImage no Linux; zip portable + one-shot cmd no Windows). Atalhos globais não são registrados no frontend Linux (X11/XWayland é o caminho futuro; Wayland puro não permite atalhos globais por design). O `PhantomVulkanLayer` é construído por CMake/`install_layer.sh` e não faz parte da solução MSBuild.

### Camada Vulkan: notas técnicas

- A memória compartilhada POSIX (`/dev/shm/phantom_fps_<pid>`, fallback `/tmp/phantom_fps_<pid>.bin` — um objeto por processo Vulkan com a layer ativa) é criada com permissão `0600` (apenas o mesmo usuário), fds com `O_CLOEXEC` e o nome é removido (`shm_unlink`/`unlink`) quando a instância Vulkan é destruída. O leitor valida se o PID autoral ainda existe em `/proc/<pid>` e ignora objetos órfãos (nada de "FPS fantasma" do processo anterior).
- O cabeçalho usa `#pragma pack(1)` e o `LinuxFpsMonitor` usa `StructLayout(Pack = 1)`: os layouts precisam permanecer idênticos (campos `uint64` ficam desalinhados, o que é aceito em x86-64/ARM64).
- O estado global é atômico (`g_last_present_ns`, `g_telemetry_shm`) e o ciclo `init/cleanup` da memória compartilhada é protegido por mutex; as escritas de `present` e o `munmap` do cleanup são sincronizados por `std::shared_mutex` (lock compartilhado para escrever, exclusivo para desmapear) — um present em voo não pode escrever depois do `munmap`.
- O repasse de funções de physical device (`Phantom_vkGetPhysicalDeviceProcAddr`, interface v2 do negotiate) encaminha cada chamada pelo GIPA da própria instância (tabela `g_instance_dispatch`), não pelo fallback global; com dois jogos simultâneos, nenhuma instância recebe funções resolvidas pela cadeia da outra.
- O leitor C# (`LinuxFpsMonitor`) se defende de leituras rasgadas: o escritor publica `instant_fps × frametime_ms ≈ 1000` (os dois vêm do mesmo delta do quadro); um par que não feche a relação indica leitura concorrente com a escrita do presents. Uma releitura no mesmo objeto resolve e, persistindo a inconsistência, o ciclo é descartado em vez de inserir um mínimo ou percentil falso nas estatísticas.

---

## 1. Visão Geral da Arquitetura

O app único (Avalonia) consome os contratos compartilhados `ISensorService` e `IFpsMonitor` do Core; a `SensorServiceFactory` escolhe os provedores por sistema operacional (nativos no Linux; Windows via LibreHardwareMonitor/PresentMon em `Core/Services/Sensors/Windows/`). O frontend WPF e sua fábrica própria foram aposentados na Fase 2.

```
PhantomMonitor.App (Linux)
  └── PhantomMonitor.Core
      ├── ISensorService / IFpsMonitor
      └── LinuxSensorService
          ├── /proc/stat (CPU)
          ├── /proc/meminfo (RAM)
          ├── /sys/class/hwmon (temperaturas e ventoinhas)
          ├── LinuxGpuSensorReader → DRM/PCI + hwmon + NVML opcional
          │   └── /proc/<pid>/fdinfo (uso i915/xe quando publicado)
          ├── /proc/diskstats e /proc/net/dev (disco e rede)
          └── LinuxFpsMonitor → PhantomVulkanLayer (memória compartilhada)

PhantomMonitor.App (Windows)
  └── PhantomMonitor.Core
      ├── Services/Sensors/Windows: HardwareMonitorService + FpsMonitor (PresentMon/ETW)
      └── Services/Platform/Windows: integração de inicialização e Win32
```

---

## 2. Leitura Nativa de Sensores do Kernel (`LinuxSensorService`)

O código fica em `Services/Sensors/Linux/`. `LinuxSensorService` coordena os ciclos e delega a coleta de CPU/memória, hwmon/energia e disco/rede a leitores internos que mantêm seus próprios contadores. A enumeração e associação de GPUs a DRM, hwmon e zonas térmicas ficam separadas em `LinuxGpuSensorReader.Discovery.cs`.

No Linux, todas as métricas são consumidas de forma não intrusiva a partir das interfaces padrão do kernel:

* **CPU (Uso e Clock):**
  * Uso: calculado a partir de deltas de tempo (user, nice, system, idle, iowait, irq, softirq, steal) em `/proc/stat`.
  * **Uso por núcleo:** mesmíssimo cálculo por linha `cpuN` publicado como `CpuPerCore#i`.
  * Clock: frequência atual de cada núcleo em `/sys/devices/system/cpu/cpu*/cpufreq/scaling_cur_freq` — `CpuClock#(i+1)` por núcleo (cpu0 → `CpuClock#1`; sem série canônica `CpuClock` no Linux, e a varredura para no primeiro núcleo sem cpufreq).
* **Memória RAM:**
  * Uso total, disponível e ocupado obtidos em `/proc/meminfo` (`MemTotal`, `MemAvailable`).
* **Temperaturas e Ventoinhas:**
  * Subsistema unificado `hwmon` em `/sys/class/hwmon/hwmon*`.
  * CPU: drivers de temperatura nativos `coretemp` (Intel), `k10temp` ou `zenpower` (AMD) — série por arquivo (`temp1_input` → `CpuTemperature#1`).
  * GPU: o coletor associa `hwmon` ao caminho PCI/DRM da placa para evitar classificar temperaturas Intel como sensores da placa-mãe. Publica `GpuTemperature#i` e, quando os rótulos do driver permitem identificar, `GpuHotSpot#i` e `GpuVramTemperature#i`; `GpuFan` e `FanSpeed#i` aparecem quando há canal `fan*_input`.
  * Controladores/EC: temperaturas por slot + canônica máxima (`MotherboardTemp`, `MotherboardTemp#i`).
* **Disco:**
  * Leitura e escrita em `/proc/diskstats`, convertendo setores (512 bytes) em throughput: canônico somado ("Discos (somatório)") + `DiskRead#i`/`DiskWrite#i` por device (slot estável por nome).
* **Rede (`/proc/net/dev`):**
  * ↑/↓ somados nas séries canônicas; `NetworkUp#i`/`NetworkDown#i` por interface (slot estável por nome, `lo` ignorado);
  * Totais da sessão: `NetworkUpTotal`/`NetworkDownTotal` (integrando a velocidade observada entre amostras).
* **GPU (Utilização, VRAM e temperatura):**
  * AMD: tenta os contadores DRM agregados `gpu_busy_percent` e `mem_info_vram_used/total`, além de temperaturas e ventoinhas `hwmon` identificadas para a placa.
  * NVIDIA: usa NVML (`libnvidia-ml.so.1`) se o driver instalado a fornecer; Nouveau e outros drivers também podem publicar métricas por DRM ou `hwmon`. Não executa `nvidia-smi` e não distribui NVML.
  * Intel: associa os canais `hwmon` por caminho PCI e calcula `GpuCore` a partir de `drm-engine-*` em `/proc/<pid>/fdinfo` quando `i915`/`xe` os publicam. O resultado representa o mecanismo mais ocupado, normalizado pelo intervalo e pela capacidade declarada pelo driver; a primeira amostra cria a base e o uso aparece no intervalo seguinte. Processos de outros usuários ou namespaces podem não estar visíveis.
  * Multi-GPU: placas DRM/PCI recebem séries `GpuCore#i`, `GpuMemoryUsed#i`, `GpuMemoryTotal#i`, `GpuTemperature#i` e outras métricas disponíveis; as chaves canônicas atuais são preservadas. Uso e total de memória vêm apenas de fontes agregadas por placa. Estatísticas por processo não são somadas (buffers podem ser compartilhados) e RAM compartilhada não substitui VRAM.
  * `GpuHotSpot` e `GpuVramTemperature` só aparecem com canais `hwmon` identificados pelo driver. Cada métrica pode ficar indisponível se a combinação de placa e driver não a publicar; **Diagnóstico** informa driver, fonte e motivo por placa e métrica. Não são necessários privilégios de administrador nem utilitários externos.
* **Bateria/Fonte AC (P6.4):**
  * `/sys/class/power_supply/BAT*` (classe `type` = `Battery`): nível (`capacity`), tensão (`voltage_now`, µV → V), potência (`power_now` µW; respatilha `current_now` × tensão) e desgaste ((1 − `energy_full`/`energy_full_design`) × 100; respatilha `charge_full(_design)`).
  * Sinal normalizado pelo arquivo `status`: **positivo consumindo**, negativo carregando (drivers divergem no sinal de `current_now`).
  * Adaptadores (`type` = `Mains`/`UPS`): `BatteryAcPower` 1/0 a partir de `online`.
  * Desktop/servidor sem `power_supply` não publica nada da família; os dados vão direto para o inventory/OSD/CSV como as demais séries.

---

## 3. Monitoramento de FPS e Frametime Sem Terceiros (`PhantomVulkanLayer`)

Para capturar taxas de quadros sem depender do MangoHud ou de injeções arriscadas, o projeto implementa uma **camada Vulkan nativa em C++** (`src/PhantomVulkanLayer/`) — **implícita** (carregada automaticamente pelo loader em todo processo Vulkan) com gating por variável de ambiente:

1. **Interceptação Transparente:**
   A camada intercepta chamadas de `vkQueuePresentKHR` diretamente na cadeia do Vulkan Loader.
2. **Cálculo de Frametime de Ultra Baixa Latência:**
   Medição com `std::chrono::steady_clock` calculando o delta em nanossegundos sem overhead perceptível (< 2 µs por quadro).
3. **Comunicação por Memória Compartilhada (IPC):**
   Os dados do quadro ativo são escritos em memória compartilhada POSIX (`/dev/shm/phantom_fps_<pid>` — um objeto por processo — com fallback para `/tmp/phantom_fps_<pid>.bin`).
4. **Consumo no C# (`LinuxFpsMonitor`):**
   O monitor C# lê os cabeçalhos diretamente da memória compartilhada, calcula médias móveis (EMA), 1% Low, 0.1% Low, máximo e alimenta o buffer circular de 600 amostras (`FrametimeBuffer`).

---

## 4. Compilação e Ativação da Camada Vulkan no Linux

### Executar o frontend:
```bash
dotnet run --project src/PhantomMonitor.App
```
O dashboard inicia a coleta automaticamente. CPU, GPU, RAM, disco e rede aparecem quando o kernel e os drivers publicam as métricas; valores sem leitura são exibidos como `—`. A área de FPS informa como ativar a camada quando ainda não recebe quadros.

### Compilação:
```bash
cd src/PhantomVulkanLayer
./install_layer.sh
```
O script compila a biblioteca `libVkLayer_phantom_monitor.so`, copia para `~/.local/lib` e registra o manifesto em **`~/.local/share/vulkan/implicit_layer.d/phantom_vulkan_layer.json`** (sufixo singular exigido pelo loader). Instalações antigas em `explicit_layers.d` (plural — nunca carregadas pelo loader) são removidas automaticamente.

### Ativação em Jogos:
A camada é **implícita**: todo processo Vulkan a carrega, mas a telemetria só é publicada quando o gating está ativo. `DISABLE_PHANTOM_OVERLAY=1` vence o enable (desativa por sessão).

* **Steam:**
  Nas propriedades do jogo, defina as Opções de Inicialização:
  ```bash
  ENABLE_PHANTOM_OVERLAY=1 %command%
  ```
* **Lutris / Heroic / Terminal:**
  ```bash
  ENABLE_PHANTOM_OVERLAY=1 ./executavel_do_jogo
  ```

## 5. Overlay flutuante

O overlay segue `showOverlay` e os campos de `OverlayConfig`. Cada bloco é uma
janela Topmost independente. Depois de abrir ou mudar de tamanho, o app mede a
janela e recalcula sua posição em pixels físicos de acordo com a escala da tela.
A aba **Diagnóstico → Resumo** registra a sessão do desktop, o backend Avalonia,
a posição solicitada e a observada e o tamanho de cada janela. Se houver
diferença, o diagnóstico a apresenta sem repetir mensagens idênticas; quando o
backend Wayland nativo estiver identificado, também explica que o compositor
pode ignorar a posição absoluta. A sessão Wayland do desktop não indica por si
só que o app usa esse backend: a configuração padrão deste projeto usa
X11/XWayland. Transparência e sobreposição em tela cheia também dependem do
compositor. No **Windows**, o overlay do
Avalonia (F1.3) é um OSD clique-through real (`WS_EX_TRANSPARENT`) que segue o
app em foco a cada 30 ms com reforço de Topmost a cada 500 ms — igual ao comportamento herdado do WPF:
não recebe cliques e não é arrastável, e o posicionamento fica com
`overlayPosition`/perfis e o botão do dashboard. No **Linux**, permanece
interativo e arrastável pelo cabeçalho, com Topmost fixo — clique-through,
follow e matcher de perfis por app são comportamentos exclusivos do Windows.
Perfis por aplicativo, templates (P6.6) e gráficos por série (P8.5) são
consumidos do `config.json` no arranque da app — ver
[overlay.md](overlay.md).

## 6. Modo dentro do jogo

**Configurações → Modo do overlay** mantém `floating` como padrão e permite
selecionar `inGame`. Configurações antigas migram para `floating`. O app publica
o conteúdo aplicado em `/dev/shm/phantommonitor_overlay_v1_<uid>` com protocolo
versionado, sequência para leitura consistente e timestamp; consumidores devem
mapear a região somente para leitura e rejeitar quadros parciais ou antigos.

O renderer in-frame Vulkan ainda não está conectado: a camada atual continua
responsável apenas por FPS/frametime. Ao escolher `inGame`, as janelas
flutuantes são ocultadas, e Diagnóstico informa que o renderer não está
disponível; não há fallback automático. A expansão para OpenGL e os hooks
Windows estão registrados no [estudo de OSD in-frame](../osd-in-frame-estudo.md).
Para FPS e para a futura camada, ative o processo explicitamente. O botão
**Iniciar** nas Configurações aplica `ENABLE_PHANTOM_OVERLAY=1` ao executável
salvo. Para a Steam, copie esta opção:

```bash
ENABLE_PHANTOM_OVERLAY=1 %command%
```

Os arquivos CSV/JSON/JSONL e o `errors.log` ficam em
`~/.local/share/PhantomMonitor/logs/`, respeitando `XDG_DATA_HOME`. A página
**Diagnóstico** mostra os registros recentes, as posições e dimensões do overlay Linux na aba **Resumo**, avisos atuais para métricas GPU indisponíveis na aba **Erros** e o estado detalhado das fontes na aba **Sensores**;
para NVIDIA, inclui driver, NVML, hwmon e métricas publicadas. O exportador HTTP
é opcional, desativado por padrão e restrito ao loopback; ativar `listenAll`
expõe os endpoints na rede sem TLS ou autenticação.
