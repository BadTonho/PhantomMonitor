# Modelos de Dados

> **Idioma:** Português (Brasil) · [English](../en/functions/data-models.md)

## SensorType

Enumeração com todos os tipos de sensores suportados:

- **CPU**: CpuTotal, CpuPerCore, CpuTemperature, CpuPower, CpuClock, CpuVoltage
- **GPU**: GpuCore, GpuMemory, GpuMemoryUsed, GpuMemoryTotal, GpuTemperature, GpuHotSpot, GpuVramTemperature (P8.6, junction da memória), GpuCoreClock, GpuMemoryClock, GpuVoltage, GpuFan, GpuPower
- **RAM**: RamUsage, RamAvailable, RamUsed, RamTotal, RamTemperature
- **Disco**: DiskUsage, DiskRead, DiskWrite, DiskTemperature, DiskHealth, DiskTotalRead, DiskTotalWritten
- **Rede**: NetworkUp, NetworkDown, NetworkUpTotal, NetworkDownTotal (totais da sessão)
- **Placa-mãe**: MotherboardTemp, FanSpeed, VrmTemperature, RailVoltage
- **Bateria (P6.4)**: BatteryLevel, BatteryVoltage, BatteryPower, BatteryWearLevel, BatteryAcPower
- **FPS/present**: Fps, FrametimeMs, FpsAvg, FpsMin, Fps1Low, Fps01Low, FpsMax, FpsP1, FpsP99, FrametimeP1, FrametimeP99, GpuBusy, CpuBusy, CpuWait, PresentLatency
- **Custom (P8.1)**: Custom — séries derivadas de fórmulas do usuário, publicadas somente como `Custom#i` (sem canônica); ver [sensores-customizados.md](sensores-customizados.md)
- **WHEA (P8.2)**: WheaErrorCount — contador de erros de hardware da sessão (Windows apenas)

Esta é a lista do modelo compartilhado, não uma garantia de que cada frontend ou
máquina publique todas as séries. `FpsP1`, `FpsP99`, `FrametimeP1`,
`FrametimeP99`, `GpuBusy`, `CpuBusy`, `CpuWait` e `PresentLatency` são fornecidos
atualmente pelo leitor Windows/PresentMon; o coletor Linux publica as métricas
de FPS disponíveis a partir da camada Vulkan, sem essas séries.

Toda categoria pode aparecer como instância `Categoria#i` (índice 0 = série canônica/agregada; ver [hardware-monitor.md](hardware-monitor.md)).

## SensorData

Modelo principal que carrega todos os dados de sensores em um snapshot:

- `Timestamp` - Momento da leitura
- `Values` - Dicionário das séries canônicas (índice 0) por tipo de sensor
- `Labels` - Rótulos das séries canônicas
- `Series` - Séries por instância, chaveadas por `SensorKey` (`GpuCore#1`, `CpuTemperature#3`...)
- `SeriesLabels` - Rótulos das séries por instância
- `SeriesStats` - Estatísticas da sessão por série: min/máx/média (ver [hardware-monitor.md](hardware-monitor.md))
- `Monitors` - Identidade dos monitores conectados (P8.4; EDID via WMI — ver MonitorInfo abaixo)
- `GpuProcesses` - Lista de processos usando GPU (não serializada no JSON)
- `SensorDebugInfo` - Informações de debug de cada sensor (não serializadas no JSON)
- `FrametimeHistory` - Array com histórico de frametime para gráficos (não serializado no JSON)

### Propriedades calculadas
- `CpuUsage`, `CpuTemp`, `GpuUsage`, `GpuTemp`
- `RamUsagePercent`, `RamAvailableGB`
- `Fps`, `FrametimeMs`, `FpsAvg`, `FpsMin`, `Fps1Low`, `Fps01Low`, `FpsMax`

## SensorKey

Chave de uma série de telemetria (`Models/SensorKey.cs`): categoria + índice de
instância, em texto `GpuCore` (canônica, índice 0) ou `GpuCore#1` (instância ≥ 1).
Serializa e deserializa como texto no snapshot JSON e nas chaves de dicionário.

- `TryParse("GpuCore#1")` / `Parse(...)` - Valida a forma textual (nome sensível a maiúsculas; `#i` exige índice ≥ 1)
- `MatchesPattern("*" | "Gpu*" | "*Total" | "CpuTemperature#*")` - Casamento com os padrões da whitelist `overlay.shownSeries` do OSD (sem distinção de maiúsculas)
- `SensorUnits.Of(type)` - Unidade de exibição por categoria (°C, %, MHz, RPM, V, W, MB, GB, B/s, B, FPS, ms, `err`)

## MonitorInfo (P8.4)

Identidade de um monitor conectado, decodificada do EDID via WMI (`root\wmi` —
`WmiMonitorID` + `WmiMonitorBasicDisplayParams`, somente monitores ativos),
ordenada pelo caminho de instância WMI. Publicada no snapshot JSON (`monitors`)
e na aba "Monitores" do inventário; CSV/SDK não mudam (são séries numéricas):

- `Manufacturer`, `Model`, `ProductCode`, `Serial` - Identidade do painel decodificada do EDID
- `DiagonalInches` - Diagonal física em polegadas (0 = não reportada)
- `YearOfManufacture`, `WeekOfManufacture` - Fabricação do painel (0 = não reportada)
- `Primary` - Monitor primário (bit `VideoInputType`; false quando não determinável)
- `DiagonalText` / `MadeText` - Formatações do inventário ("27.5\"", "2023 s24"; `--` quando ausente)

## AlertRule / AlertsConfig

Regras de alerta sobre sensores (ver [alertas.md](alertas.md)):

- `AlertComparison` - `Above` (acima do limiar) ou `Below` (abaixo do limiar)
- `AlertRule` - `Enabled`, `Sensor` (qualquer `SensorType`), `SensorIndex` (0 = série canônica; ≥ 1 mira uma instância, ex. `GpuTemperature` índice 1 = GPU dedicada), `Comparison`, `Threshold`, `CooldownSeconds` (0 = global), `Popup`, `Sound`, `Script`, `ScriptArguments`, `Shutdown`, `Message`
- `AlertsConfig` - `Enabled`, `CooldownSeconds` (padrão global), `AllowShutdown` (trava da ação de desligar) e `Rules`

## GpuProcessInfo

Modelo para processos que estão usando a GPU:

- `Name` - Nome do processo
- `Usage` - Percentual de uso da GPU
- `MemoryMb` - Memória GPU utilizada em MB
