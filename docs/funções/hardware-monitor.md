# HardwareMonitorService

> **Idioma:** Português (Brasil) · [English](../en/functions/hardware-monitor.md)
 
Serviço de leitura de sensores de hardware para ambiente Windows, implementando a interface `ISensorService`. Utiliza a biblioteca LibreHardwareMonitor para acessar sensores de CPU, GPU, memória, disco, placa-mãe e rede, com fallbacks via WMI (Windows Management Instrumentation) quando necessário. A fonte vive em `PhantomMonitor.Core/Services/Sensors/Windows/` e é escolhida pela fábrica multiplataforma [`SensorServiceFactory`](../../../src/PhantomMonitor.Core/Services/Sensors/SensorServiceFactory.cs) do app único; no Linux o mesmo Core entrega provedores nativos em `Core/Services/Sensors/Linux/`. Anotado com `[SupportedOSPlatform("windows")]` — APIs Windows-only (WMI/ETW/PerformanceCounter) instanciam apenas no lado Windows (guard de OS na fábrica).

## Funcionalidades

No Core, o serviço fica em `Services/Sensors/Windows/`; fallbacks WMI, identificação de monitores e consulta de processos da GPU estão em arquivos parciais da mesma classe para manter a coordenação e o estado compartilhado.

- Leitura de temperatura, uso, clock, voltagem e potência da CPU
- Leitura de temperatura, uso, clock e potência da GPU
- **Temperaturas por diodo da GPU:** `GpuHotSpot` (Hot Spot) e `GpuVramTemperature` (junction da memória — NVIDIA `GPU Memory Temperature`/legado `Memory Junction`, AMD `GPU VRAM`); diodos `VRM`/`Liquid`/`PLX` da AMD não publicam série (`GpuTemperature` só aceita diodos do chip)
- Leitura de VRAM usada/total (`SmallData`: `GPU Memory Used/Total` em MB; reserva `D3D Dedicated Memory Used/Total` para GPUs integradas)
- Leitura de uso e disponibilidade de RAM
- Leitura de uso e I/O de disco
- Leitura de velocidade de ventoinhas e temperatura da placa-mãe (inclui sensores de sub-hardware/SuperIO)
- **Fallback de clock da CPU:** quando o LibreHardwareMonitor não publica frequência positiva, `CallNtPowerInformation(ProcessorInformation)` fornece `CurrentMhz` por processador lógico; a média é publicada na série existente `CpuClock` e identificada como aproximada. Esse valor reflete a frequência máxima ajustada pelo throttling atual, não é clock por núcleo e não preenche `CpuClock#i`. A leitura do LHM tem prioridade; nenhuma fonte de frequência válida deixa o clock indisponível. O acesso completo aos sensores pode exigir iniciar manualmente como Administrador; o app não solicita elevação automaticamente.
- **Fallback de temperatura da CPU via zona térmica ACPI (WMI `root\WMI`) e, em cascata, o contador térmico do sistema (`Win32_PerfFormattedData_Counters_ThermalZoneInformation`), **somente se o LibreHardwareMonitor não fornecer** a temperatura; consultado no máximo a cada 10 s e rotulado como aproximação
- Fallback de RAM via WMI (`Win32_OperatingSystem`) e `PerformanceCounter` reutilizado entre ciclos
- Processos que usam a GPU via WMI (`GPUEngine`), agregados por PID, com nome resolvido e cache de 3 s
- **Bateria (P6.4):** notebooks/UPs publicam a família `Battery*` — carga estimada, tensão, fluxo de potência, desgaste e status da fonte AC; consultas WMI têm cache de 10 s e desktops não publicam nenhuma série da família (ver seção abaixo). Classes ausentes são marcadas como indisponíveis durante a execução, sem erros repetidos; os outros provedores de bateria continuam como fallback.
- **Monitores (P8.4):** identidade dos painéis conectados via EDID/WMI (`WmiMonitorID` + `WmiMonitorBasicDisplayParams`, somente monitores ativos) — fabricante, modelo, serial, diagonal física, ano/semana de fabricação e flag de primário; cache de 60 s, lista ordenada pelo caminho de instância WMI. Campos opcionais são tolerados; se a consulta WMI falhar, o app continua com a lista vazia e registra uma única ocorrência diagnóstica por execução. Sem monitores reportados também resulta em lista vazia, sem erro. Modelo `MonitorInfo` em [modelos-dados.md](modelos-dados.md)
- **Erros de hardware WHEA:** série `WheaErrorCount` (Windows apenas) = eventos do provedor `Microsoft-Windows-WHEA-Logger` no log System desde o início da sessão; `0` é leitura real (alerta `Above > 0` detecta que o contador ficou não zero; como ele é cumulativo, pode repetir após cooldown, mas não sinaliza cada incremento separadamente)

## Regras de mapeamento de sensores

- **Temperatura da CPU:** prioridade `Package/Tctl/Tdie/Average` > `Max` > demais.
- **Temperatura da GPU:** `GPU Core` tem prioridade sobre os demais diodos; `Memory`/`VRAM` (junction da memória) vai para `GpuVramTemperature` e diodos `VRM`/`Liquid`/`PLX` ficam sem série. GPU Intel integrada é processada antes, e a GPU dedicada prevalece.
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

A lista de séries do snapshot é pré-computada uma única vez por ciclo, após a
última escrita em valores/séries (fórmulas custom incluídas) e distribuída aos
consumidores — overlay, inventário, journal, CSV e estatísticas compartilham a
mesma lista ordenada (séries canônicas primeiro na ordem do enum, depois
instâncias ordenadas). `SensorData.GetSeriesKeys()` devolve essa lista quando
presente e reconstrói do jeito antigo em snapshots legados.

## Concorrência e ciclo de vida

- `Start/Stop/Update/Dispose` são serializados por um lock; um tick que chega durante outro ciclo é descartado.
- `Stop()` e `Dispose()` são idempotentes; `SensorUpdated` é disparado **fora** do lock.
- Exceções são registradas em `logs/errors.log` via `ErrorLog` (sem `catch` vazios).

## Bateria (`Battery*`, P6.4)

Família publicada **apenas quando existe bateria** (notebook/UPS); desktop não
gera nenhuma linha — nem no CSV, nem no JSON, nem no OSD:

| Série | Unidade | Significado / fonte |
|---|---|---|
| `BatteryLevel` | % | Carga remanescente (`Win32_Battery.EstimatedChargeRemaining`) |
| `BatteryVoltage` | V | Tensão da bateria (`BatteryStatus.Voltage`, mV → V; respatilha `DesignVoltage`) |
| `BatteryPower` | W | Fluxo de potência: **positivo consumindo** (discharge), **negativo carregando** (`DischargeRate`/`ChargeRate`, mW → W) |
| `BatteryWearLevel` | % | Desgaste = (1 − FullChargedCapacity/DesignedCapacity) × 100 (root\WMI; respatilha `Win32_Battery` Full/Design) |
| `BatteryAcPower` | 1/0 | Fonte AC conectada (`PowerOnline`) |

- Leitura WMI a cada **10 s** (carga/tensão mudam devagar; a consulta atravessa COM).
- `SELECT *` com acesso tolerante às propriedades: drivers de bateria variam o que
  expõem (propriedade ausente devolve fallback em vez de derrubar o ciclo).
- Classes opcionais inexistentes (`InvalidClass`) são lembradas pelo restante da
  execução e deixam de ser consultadas; `Win32_Battery` e as demais classes
  disponíveis continuam alimentando os fallbacks.
- OSD: família `BAT` com cor lima própria (só aparece com `Battery*` na whitelist
  `overlay.shownSeries`).

## Erros de hardware WHEA (`WheaErrorCount`)

Contador **Windows-apenas** de erros de hardware reportados pela Windows Hardware
Error Architecture: eventos do provedor **`Microsoft-Windows-WHEA-Logger`** no log
System (falhas de PCIe/device corrigidas ou fatais, CPU, memória, cache...).

| Série | Unidade | Significado |
|---|---|---|
| `WheaErrorCount` | err | Nº de eventos WHEA **desde o início da sessão de monitoramento** |

- Publicada **sempre** (contador em `0` é leitura real, não série ausente). Uma
  regra `Above` com limite `0` detecta que o contador da sessão ficou acima de
  zero; dispara na transição para verdadeiro e pode repetir após o cooldown
  enquanto continuar acima de zero, mas não sinaliza cada incremento
  separadamente.
- Consulta ao log de eventos **a cada 10 s**, direção reversa com early-break
  (para no primeiro evento anterior ao início da sessão; teto de 5000 eventos).
- Sem permissão de leitura ou provedor ausente: contador segue em `0` e o erro é
  registrado em `logs/errors.log` (`Whea.Reader`).

## Sensores customizados (`Custom#i`)

Fórmulas do usuário derivadas de outras séries, avaliadas no fim de cada snapshot
(antes do commit das estatísticas) — detalhes completos em
[sensores-customizados.md](sensores-customizados.md).

## Principais métodos

- `Start()` - Inicia o monitoramento dos sensores
- `Stop()` - Para o monitoramento
- `GetCurrentSnapshot()` - Retorna o snapshot mais recente dos sensores
- `ResetSeriesStats()` - Zera as estatísticas por série da sessão (min/máx/média)
- `SetPaused(bool) / Paused` - Pausa/retoma os ciclos (P6.7): sem eventos, o último
  snapshot permanece para overlay/inventário/SDK/HTTP; não persiste no config
- `Dispose()` - Para e libera timer, FPS monitor e contadores

## Estatísticas por série (min/max/média)

A cada ciclo publicado, o `SeriesStatsTracker` acumula **uma amostra por série**
(canônicas e por instância) — min, máx e média aritmética da
sessão. O resultado viaja no snapshot (`SensorData.SeriesStats`) e por isso chega
ao inventário, ao JSON (`sensors_latest.json`) e a qualquer consumidor da API.
Amostras não-finitas (NaN/Inf) são descartadas (números de leitura "balançada"
não contaminam a média para sempre). As séries de FPS publicadas no snapshot
(`Fps`, `FrametimeMs`, `FpsP1`...) também entram no agregador (o `Commit` roda
depois da coleta do FpsMonitor); as janelas móveis de ~60 s que produzem
`FpsAvg`/min/percentis são independentes do `SeriesStatsTracker`.

Com ajuste por série (**P9.1**), o `Commit` recebe o conjunto de séries
**repetidas** do `SensorTuningEngine` e **não conta** essas amostras: quando uma
série tem `intervalMs` próprio e repete o último valor entre refreshes, min/máx
/média amostram na cadência dela — o tick global não duplica pontos (ver
[ajuste-de-series.md](ajuste-de-series.md)).

- **Reset:** item da bandeja ("Resetar estatísticas") e botão da janela de
  inventário; o CSV ativo recebe um marcador `#marker` delimitando o segmento
  (ver [logging.md](logging.md)).
- `Legendas`: a média só fica definida (`"--"`) com ao menos uma amostra.

## Eventos

- `SensorUpdated` - Disparado a cada ciclo de atualização com os dados mais recentes. A interface o consome via `Dispatcher.InvokeAsync` (não bloqueia a thread do timer).
