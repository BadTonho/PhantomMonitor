# HardwareMonitorService
 
Serviço de leitura de sensores de hardware para ambiente Windows, implementando a interface `ISensorService`. Utiliza a biblioteca LibreHardwareMonitor para acessar sensores de CPU, GPU, memória, disco, placa-mãe e rede, com fallbacks via WMI (Windows Management Instrumentation) quando necessário. No Linux, a `SensorServiceFactory` instancia automaticamente o `LinuxSensorService`.

## Funcionalidades

- Leitura de temperatura, uso, clock, voltagem e potência da CPU
- Leitura de temperatura, uso, clock e potência da GPU
- Leitura de VRAM usada/total (`SmallData`: `GPU Memory Used/Total` em MB; reserva `D3D Dedicated Memory Used/Total` para GPUs integradas)
- Leitura de uso e disponibilidade de RAM
- Leitura de uso e I/O de disco
- Leitura de velocidade de ventoinhas e temperatura da placa-mãe (inclui sensores de sub-hardware/SuperIO)
- Fallback de temperatura da CPU via zona térmica ACPI (WMI `root\WMI`), **somente se o LibreHardwareMonitor não fornecer** a temperatura; consultado no máximo a cada 10 s e rotulado como aproximação
- Fallback de RAM via WMI (`Win32_OperatingSystem`) e `PerformanceCounter` reutilizado entre ciclos
- Processos que usam a GPU via WMI (`GPUEngine`), agregados por PID, com nome resolvido e cache de 3 s

## Regras de mapeamento de sensores

- **Temperatura da CPU:** prioridade `Package/Tctl/Tdie/Average` > `Max` > demais.
- **Temperatura da GPU:** `GPU Core` tem prioridade sobre `Memory Junction` etc.; GPU Intel integrada é processada antes, e a GPU dedicada prevalece.
- **Vários núcleos/discos:** uso por núcleo, uso de disco, clock, ventoinhas e temperatura da placa-mãe guardam o **maior** valor; throughput de disco e rede é **somado**.
- **VRAM da GPU:** prioridade `GPU Memory Used/Total` > `D3D Dedicated Memory Used/Total` (sensores `SmallData`, em MB).
- **Disco (`DiskUsage`):** usa o sensor `Total Activity` (% de atividade), não `Used Space`.
- **RAM:** somente memória física (ignora `Virtual Memory`). Os sensores `Data` do LibreHardwareMonitor já vêm em **GB**; `RamTotal` = usada + disponível.

## Séries por instância (`SensorKey`)

Além das séries canônicas (índice 0, acima), cada ciclo também publica séries
**por instância** com chaves `Categoria#índice` (`GpuCore#1`, `CpuTemperature#3`,
`DiskRead#2`...). A dedicada é sempre a GPU 1; discos e interfaces recebem
índice na ordem em que o LibreHardwareMonitor os enumera.

| Categoria | Instância | Fonte |
|---|---|---|
| `CpuPerCore#i`, `CpuTemperature#i`, `CpuClock#i` | núcleo índice `i` (`#` no nome do sensor) | CPU |
| `CpuPower#i` | dominio de potência extra ("IA Cores", "GT Cores"...) | CPU/MSR |
| `GpuCore#i`... (todas as séries de GPU) | GPU dedicada = 1, integrada = 2, 3... | GPU |
| `FanSpeed#i` | ventoinha por slot (placa-mãe/SuperIO/EC) | "Fan #N"/nome |
| `DiskUsage/DiskRead/DiskWrite/DiskTemperature/DiskHealth/DiskTotalRead/DiskTotalWritten#i` | disco por nó | NVMe/HD SMART |
| `NetworkUp/NetworkDown#i` | interface por nó (nickname do adaptador no rótulo) | NIC |
| `MotherboardTemp#i`, `VrmTemperature#i`, `RailVoltage#i`, `RamTemperature#i` | slot estável por nome do sensor | Placa-mãe/SuperIO/EC |
| `NetworkUpTotal`, `NetworkDownTotal` (canônicas) | somatório da sessão (velocidade × tempo entre amostras) | NIC |

Consumidores:
- **CSV/JSON:** colunas/entradas `Categoria#i` automáticas (ver [logging.md](logging.md)).
- **OSD:** linhas por série filtradas pela whitelist `overlay.shownSeries` (ver [configuracao.md](configuracao.md)).
- **Alertas:** `rules[].sensorIndex` mira uma instância (`GpuTemperature` + índice 1, ver [alertas.md](alertas.md)).

## Concorrência e ciclo de vida

- `Start/Stop/Update/Dispose` são serializados por um lock; um tick que chega durante outro ciclo é descartado.
- `Stop()` e `Dispose()` são idempotentes; `SensorUpdated` é disparado **fora** do lock.
- Exceções são registradas em `logs/errors.log` via `ErrorLog` (sem `catch` vazios).

## Principais métodos

- `Start()` - Inicia o monitoramento dos sensores
- `Stop()` - Para o monitoramento
- `GetCurrentSnapshot()` - Retorna o snapshot mais recente dos sensores
- `Dispose()` - Para e libera timer, FPS monitor e contadores

## Eventos

- `SensorUpdated` - Disparado a cada ciclo de atualização com os dados mais recentes. A interface o consome via `Dispatcher.InvokeAsync` (não bloqueia a thread do timer).
