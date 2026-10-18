# Suporte Nativo a Linux (Multiplataforma)

O **Riva Monitor** oferece suporte arquitetural e operacional para Linux mantendo rigorosamente a premissa fundamental do projeto: **independência total de softwares ou utilitários de terceiros** (sem depender de MangoHud, RTSS, GOverlay ou drivers proprietários adicionais).

> [!IMPORTANT]
> **Escopo atual:** o app WPF (`RivaMonitor.csproj`, `net10.0-windows`) só executa no Windows. O código em `Services/Linux/` e a camada `RivaVulkanLayer` são a base de suporte a Linux, mas **ainda não há um front-end Linux executável** (seria necessário um projeto multiplataforma ou multi-target). O `RivaVulkanLayer` é construído por CMake/`install_layer.sh` e **não faz parte do `RivaMonitor.slnx`** (a solução só lista projetos MSBuild).

### Camada Vulkan: notas técnicas

- A memória compartilhada (`/dev/shm/riva_fps`, fallback `/tmp/riva_fps.bin`) é criada com permissão `0600` (apenas o mesmo usuário).
- O cabeçalho usa `#pragma pack(1)` e o `LinuxFpsMonitor` usa `StructLayout(Pack = 1)`: os layouts precisam permanecer idênticos (campos `uint64` ficam desalinhados, o que é aceito em x86-64/ARM64).
- O estado global é atômico (`g_last_present_ns`, `g_telemetry_shm`) e `init/cleanup` da memória compartilhada é protegido por mutex.
- Limitação: há um único bloco de telemetria compartilhado. Se vários processos Vulkan com a camada ativa rodarem ao mesmo tempo, o último a inicializar prevalece.

---

## 1. Visão Geral da Arquitetura

O sistema emprega uma arquitetura desacoplada via `ISensorService` e `IFpsMonitor`, com seleção dinâmica de provedor em tempo de execução através da `SensorServiceFactory`:

```
                    ┌─────────────────────────┐
                    │    RivaMonitor (UI)     │
                    └────────────┬────────────┘
                                 │
                     ISensorService / IFpsMonitor
                                 │
            ┌────────────────────┴────────────────────┐
            ▼                                         ▼
   [Ambiente Windows]                         [Ambiente Linux]
 ├── HardwareMonitorService                 ├── LinuxSensorService
 │   └── LibreHardwareMonitor               │   ├── /proc/stat (CPU usage)
 └── FpsMonitor                             │   ├── /proc/meminfo (RAM)
     └── Intel PresentMon                   │   ├── /sys/class/hwmon (Temps)
                                            │   ├── /sys/class/drm (GPU usage/VRAM)
                                            │   └── /proc/diskstats (I/O)
                                            └── LinuxFpsMonitor
                                                └── RivaVulkanLayer (shared memory)
```

---

## 2. Leitura Nativa de Sensores do Kernel (`LinuxSensorService`)

No Linux, todas as métricas são consumidas de forma não intrusiva a partir das interfaces padrão do kernel:

* **CPU (Uso e Clock):**
  * Uso: calculado a partir de deltas de tempo (user, nice, system, idle, iowait) em `/proc/stat`.
  * Clock: frequência atual de cada núcleo em `/sys/devices/system/cpu/cpu*/cpufreq/scaling_cur_freq`.
* **Memória RAM:**
  * Uso total, disponível e ocupado obtidos em `/proc/meminfo` (`MemTotal`, `MemAvailable`).
* **Temperaturas e Ventoinhas:**
  * Subsistema unificado `hwmon` em `/sys/class/hwmon/hwmon*`.
  * CPU: drivers de temperatura nativos `coretemp` (Intel), `k10temp` ou `zenpower` (AMD).
  * GPU: drivers de kernel DRM (`amdgpu`, `nouveau`, `nvidia`).
* **Disco:**
  * Leitura e escrita em `/proc/diskstats`, convertendo setores (512 bytes) em throughput de MB/s.
* **GPU (Utilização e VRAM):**
  * Monitoramento DRM em `/sys/class/drm/card*/device/gpu_busy_percent` e `/sys/class/drm/card*/device/mem_info_vram_used`.

---

## 3. Monitoramento de FPS e Frametime Sem Terceiros (`RivaVulkanLayer`)

Para capturar taxas de quadros sem depender do MangoHud ou de injeções arriscadas, o projeto implementa uma **camada Vulkan explícita nativa em C++** (`src/RivaVulkanLayer/`):

1. **Interceptação Transparente:**
   A camada intercepta chamadas de `vkQueuePresentKHR` diretamente na cadeia do Vulkan Loader.
2. **Cálculo de Frametime de Ultra Baixa Latência:**
   Medição com `clock_gettime(CLOCK_MONOTONIC)` calculando o delta em microssegundos sem overhead perceptível (< 2 µs por quadro).
3. **Comunicação por Memória Compartilhada (IPC):**
   Os dados do quadro ativo são escritos em memória compartilhada POSIX (`/dev/shm/riva_fps` com fallback para `/tmp/riva_fps.bin`).
4. **Consumo no C# (`LinuxFpsMonitor`):**
   O monitor C# lê os cabeçalhos diretamente da memória compartilhada, calcula médias móveis (EMA), 1% Low e alimenta o buffer circular de 600 amostras (`FrametimeBuffer`).

---

## 4. Compilação e Ativação da Camada Vulkan no Linux

### Compilação:
```bash
cd src/RivaVulkanLayer
./install_layer.sh
```
O script compilará a biblioteca `libVkLayer_riva_monitor.so` e registrará o manifesto em `~/.local/share/vulkan/explicit_layers.d/riva_vulkan_layer.json`.

### Ativação em Jogos:
* **Steam:**
  Nas propriedades do jogo, defina as Opções de Inicialização:
  ```bash
  ENABLE_RIVA_OVERLAY=1 %command%
  ```
* **Lutris / Heroic / Terminal:**
  ```bash
  ENABLE_RIVA_OVERLAY=1 ./executavel_do_jogo
  ```
