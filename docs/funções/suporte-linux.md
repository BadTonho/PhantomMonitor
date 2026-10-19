# Suporte Nativo a Linux (Multiplataforma)

O **Phantom Monitor** oferece suporte arquitetural e operacional para Linux mantendo rigorosamente a premissa fundamental do projeto: **independência total de softwares ou utilitários de terceiros** (sem depender de MangoHud, GOverlay ou drivers proprietários adicionais).

> [!IMPORTANT]
> **Escopo atual:** o app WPF (`PhantomMonitor.csproj`, `net10.0-windows`) só executa no Windows. O código em `Services/Linux/` e a camada `PhantomVulkanLayer` são a base de suporte a Linux, mas **ainda não há um front-end Linux executável** (seria necessário um projeto multiplataforma ou multi-target). O `PhantomVulkanLayer` é construído por CMake/`install_layer.sh` e **não faz parte do `PhantomMonitor.slnx`** (a solução só lista projetos MSBuild).

### Camada Vulkan: notas técnicas

- A memória compartilhada POSIX (`/dev/shm/phantom_fps_<pid>`, fallback `/tmp/phantom_fps_<pid>.bin` — um objeto por processo Vulkan com a layer ativa) é criada com permissão `0600` (apenas o mesmo usuário), fds com `O_CLOEXEC` e o nome é removido (`shm_unlink`/`unlink`) quando a instância Vulkan é destruída. O leitor valida se o PID autoral ainda existe em `/proc/<pid>` e ignora objetos órfãos (nada de "FPS fantasma" do processo anterior).
- O cabeçalho usa `#pragma pack(1)` e o `LinuxFpsMonitor` usa `StructLayout(Pack = 1)`: os layouts precisam permanecer idênticos (campos `uint64` ficam desalinhados, o que é aceito em x86-64/ARM64).
- O estado global é atômico (`g_last_present_ns`, `g_telemetry_shm`) e o ciclo `init/cleanup` da memória compartilhada é protegido por mutex; as escritas de `present` e o `munmap` do cleanup são sincronizados por `std::shared_mutex` (lock compartilhado para escrever, exclusivo para desmapear) — um present em voo não pode escrever depois do `munmap`.

---

## 1. Visão Geral da Arquitetura

O sistema emprega uma arquitetura desacoplada via `ISensorService` e `IFpsMonitor`, com seleção dinâmica de provedor em tempo de execução através da `SensorServiceFactory`:

```
                    ┌─────────────────────────┐
                    │   PhantomMonitor (UI)   │
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
                                                └── PhantomVulkanLayer (shared memory)
```

---

## 2. Leitura Nativa de Sensores do Kernel (`LinuxSensorService`)

No Linux, todas as métricas são consumidas de forma não intrusiva a partir das interfaces padrão do kernel:

* **CPU (Uso e Clock):**
  * Uso: calculado a partir de deltas de tempo (user, nice, system, idle, iowait) em `/proc/stat`.
  * **Uso por núcleo:** mesmíssimo cálculo por linha `cpuN` publicado como `CpuPerCore#i`.
  * Clock: frequência atual de cada núcleo em `/sys/devices/system/cpu/cpu*/cpufreq/scaling_cur_freq` — canônica (cpu0) + `CpuClock#(i+1)` por núcleo.
* **Memória RAM:**
  * Uso total, disponível e ocupado obtidos em `/proc/meminfo` (`MemTotal`, `MemAvailable`).
* **Temperaturas e Ventoinhas:**
  * Subsistema unificado `hwmon` em `/sys/class/hwmon/hwmon*`.
  * CPU: drivers de temperatura nativos `coretemp` (Intel), `k10temp` ou `zenpower` (AMD) — série por arquivo (`temp1_input` → `CpuTemperature#1`).
  * GPU: drivers de kernel DRM (`amdgpu`, `nouveau`, `nvidia`) — `GpuTemperature#hwmon` por card; `GpuFan` canônico + `FanSpeed#hwmon` por ventoinha.
  * Controladores/EC: temperaturas por slot + canônica máxima (`MotherboardTemp`, `MotherboardTemp#i`).
* **Disco:**
  * Leitura e escrita em `/proc/diskstats`, convertendo setores (512 bytes) em throughput: canônico somado ("Discos (somatório)") + `DiskRead#i`/`DiskWrite#i` por device (slot estável por nome).
* **Rede (`/proc/net/dev`):**
  * ↑/↓ somados nas séries canônicas; `NetworkUp#i`/`NetworkDown#i` por interface (slot estável por nome, `lo` ignorado);
  * Totais da sessão: `NetworkUpTotal`/`NetworkDownTotal` (integrando a velocidade observada entre amostras).
* **GPU (Utilização e VRAM):**
  * Monitoramento DRM em `/sys/class/drm/card*/device/gpu_busy_percent` e `mem_info_vram_*` — multi-GPU real: canônica do primeiro card + `GpuCore#i`, `GpuMemoryUsed#i`, `GpuMemoryTotal#i` para cada card.
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
