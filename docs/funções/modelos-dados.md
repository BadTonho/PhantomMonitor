# Modelos de Dados

## SensorType

Enumeração com todos os tipos de sensores suportados:

- **CPU**: CpuTotal, CpuPerCore, CpuTemperature, CpuPower, CpuClock, CpuVoltage
- **GPU**: GpuCore, GpuMemory, GpuTemperature, GpuHotSpot, GpuCoreClock, GpuMemoryClock, GpuVoltage, GpuFan, GpuPower
- **RAM**: RamUsage, RamAvailable, RamUsed, RamTotal
- **Disco**: DiskUsage, DiskRead, DiskWrite
- **Rede**: NetworkUp, NetworkDown
- **Outros**: MotherboardTemp, FanSpeed
- **FPS**: Fps, FrametimeMs, FpsAvg, FpsMin, Fps1Low

## SensorData

Modelo principal que carrega todos os dados de sensores em um snapshot:

- `Timestamp` - Momento da leitura
- `Values` - Dicionário de valores por tipo de sensor
- `Labels` - Rótulos dos sensores
- `GpuProcesses` - Lista de processos usando GPU
- `SensorDebugInfo` - Informações de debug de cada sensor
- `FrametimeHistory` - Array com histórico de frametime para gráficos

### Propriedades calculadas
- `CpuUsage`, `CpuTemp`, `GpuUsage`, `GpuTemp`
- `RamUsagePercent`, `RamAvailableGB`
- `Fps`, `FrametimeMs`, `FpsAvg`, `FpsMin`, `Fps1Low`

## GpuProcessInfo

Modelo para processos que estão usando a GPU:

- `Name` - Nome do processo
- `Usage` - Percentual de uso da GPU
- `MemoryMb` - Memória GPU utilizada em MB
