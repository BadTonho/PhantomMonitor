# PhantomSensorLib (C++ DLL) / SensorBridge

DLL nativa em C++ para leitura de sensores. **Atualmente é um stub**: todas as leituras retornam `0.0`. A leitura real de sensores no Windows é feita em C# pelo `HardwareMonitorService` (LibreHardwareMonitor); a DLL é opcional e o app funciona sem ela.

## Contrato nativo (ABI)

Definido em `src/PhantomSensorLib/PhantomSensorLib.h` e espelhado em `src/PhantomMonitor/Native/SensorBridge.cs`. **Os dois lados precisam permanecer idênticos.**

### `NativeSensorSnapshot` (16 × `double` = 128 bytes, sem padding)

`CpuUsage, CpuTemp, CpuClock, CpuVoltage, CpuPower, GpuUsage, GpuTemp, GpuCoreClock, GpuMemoryClock, GpuVoltage, GpuMemoryUsed, GpuMemoryTotal, GpuPower, RamUsage, RamTotal, FanSpeedRpm`

- No C++, um `static_assert` garante `sizeof == 128`.
- No C#, `SensorBridge.TryLoad()` valida `Marshal.SizeOf<NativeSensorSnapshot>() == 128` antes de usar a DLL (evita estouro de buffer se os layouts divergirem).
- O enum `NativeSensorType` segue a mesma ordem dos campos (índices 0–15) usada por `PhantomSensor_GetSensorName` / `PhantomSensor_GetSensorValue`.

### Funções exportadas (`extern "C"`, cdecl)

| Função | Descrição |
|---|---|
| `PhantomSensor_Init()` | Inicializa a biblioteca (retorna 0 em sucesso) |
| `PhantomSensor_ReadSnapshot(NativeSensorSnapshot*)` | Zera e preenche o snapshot (stub) |
| `PhantomSensor_GetSensorCount()` | Retorna 16 |
| `PhantomSensor_GetSensorName(index, buffer, size)` | Nome do sensor (valida índice e buffer) |
| `PhantomSensor_GetSensorValue(index, double*)` | Valor do sensor (valida índice) |
| `PhantomSensor_Shutdown()` | Finaliza a biblioteca |

### API C# (`SensorBridge`)

`TryLoad()`, `ReadSnapshot()`, `GetSensorName(int)`, `Unload()` e `IsAvailable`. `TryLoad` captura `DllNotFoundException`, `EntryPointNotFoundException` e `BadImageFormatException`.

## Build

O target `BuildPhantomSensorLib` do `PhantomMonitor.csproj` compila a DLL antes do build, **apenas se o `.cpp`/`.h` forem mais novos que a DLL** (build incremental):

1. Localiza `vcvars64.bat` (VS 2022 BuildTools, Community, Professional ou Enterprise). Pode ser informado manualmente: `dotnet build -p:VcVarsPath="C:\...\vcvars64.bat"`.
2. Executa `call vcvars64.bat` + `cl /LD /EHsc /std:c++17 /DPHANTOMSENSORLIB_EXPORTS`. O `PHANTOMSENSORLIB_EXPORTS` é obrigatório para o header usar `dllexport`.
3. Copia `PhantomSensorLib.dll` para o diretório de saída.

Se o MSVC não for encontrado ou o `cl` falhar, o build emite **aviso** (não erro) e o app segue sem a DLL. Alternativa: `CMakeLists.txt` em `src/PhantomSensorLib/`.

## Próximos passos

- Implementar leitura real (NVML para GPU NVIDIA, ACPI/WMI direto etc.)
- Hook DirectX (`IDXGISwapChain::Present`) para captura de FPS independente
