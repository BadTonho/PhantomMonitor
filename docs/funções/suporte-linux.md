# Suporte Nativo a Linux (Multiplataforma)

O **Phantom Monitor** oferece suporte arquitetural e operacional para Linux mantendo rigorosamente a premissa fundamental do projeto: **independência total de softwares ou utilitários de terceiros** (sem depender de MangoHud, GOverlay ou drivers proprietários adicionais).

> [!IMPORTANT]
> **Escopo atual:** o dashboard Avalonia (`src/PhantomMonitor.App`) é, nesta etapa, o frontend Linux e consome `LinuxSensorService` de `PhantomMonitor.Core`. Ele cobre monitoramento ao vivo, FPS, inventário de séries publicadas, gravação CSV/JSON/JSONL, configurações de coleta/logs e edição de alertas com notificações internas. As regras são salvas no bloco `alerts` do `config.json`; ações de som, scripts e desligamento são preservadas no arquivo, mas não executadas no Avalonia. O frontend WPF (`src/PhantomMonitor`) permanece disponível no Windows durante a transição com suas ações de alerta atuais; ainda não há provedor Windows ligado ao app Avalonia. Overlay e bandeja continuam no WPF. O `PhantomVulkanLayer` é construído por CMake/`install_layer.sh` e não faz parte da solução MSBuild.

### Camada Vulkan: notas técnicas

- A memória compartilhada POSIX (`/dev/shm/phantom_fps_<pid>`, fallback `/tmp/phantom_fps_<pid>.bin` — um objeto por processo Vulkan com a layer ativa) é criada com permissão `0600` (apenas o mesmo usuário), fds com `O_CLOEXEC` e o nome é removido (`shm_unlink`/`unlink`) quando a instância Vulkan é destruída. O leitor valida se o PID autoral ainda existe em `/proc/<pid>` e ignora objetos órfãos (nada de "FPS fantasma" do processo anterior).
- O cabeçalho usa `#pragma pack(1)` e o `LinuxFpsMonitor` usa `StructLayout(Pack = 1)`: os layouts precisam permanecer idênticos (campos `uint64` ficam desalinhados, o que é aceito em x86-64/ARM64).
- O estado global é atômico (`g_last_present_ns`, `g_telemetry_shm`) e o ciclo `init/cleanup` da memória compartilhada é protegido por mutex; as escritas de `present` e o `munmap` do cleanup são sincronizados por `std::shared_mutex` (lock compartilhado para escrever, exclusivo para desmapear) — um present em voo não pode escrever depois do `munmap`.

---

## 1. Visão Geral da Arquitetura

O app Avalonia Linux consome os contratos compartilhados `ISensorService` e `IFpsMonitor` do Core. O app WPF mantém sua fábrica para Windows e os serviços de plataforma existentes.

```
PhantomMonitor.App (Avalonia/Linux)
  └── PhantomMonitor.Core
      ├── ISensorService / IFpsMonitor
      └── LinuxSensorService
          ├── /proc/stat (CPU)
          ├── /proc/meminfo (RAM)
          ├── /sys/class/hwmon (temperaturas e ventoinhas)
          ├── /sys/class/drm (GPU e VRAM)
          ├── /proc/diskstats e /proc/net/dev (disco e rede)
          └── LinuxFpsMonitor → PhantomVulkanLayer (memória compartilhada)

PhantomMonitor (WPF/Windows)
  └── HardwareMonitorService + FpsMonitor (PresentMon/ETW)
```

---

## 2. Leitura Nativa de Sensores do Kernel (`LinuxSensorService`)

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
  * GPU: drivers de kernel DRM (`amdgpu`, `nouveau`, `nvidia`) — `GpuTemperature#hwmon` por card; `GpuFan` canônico + `FanSpeed#(hwmon×100 + fanN)` por ventoinha (slot composto: hwmon e o N do arquivo `fan*_input`). O arquivo `tempN_label` diferencia os diodos quando existe: `edge` → `GpuTemperature`, `junction`/`hotspot` → `GpuHotSpot`, `mem`/`vram` → `GpuVramTemperature` (canônicas = maior valor entre os cards).
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
