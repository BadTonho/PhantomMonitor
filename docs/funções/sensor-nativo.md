# RivaSensorLib (C++ DLL) / SensorBridge

DLL nativa em C++ para leitura de sensores. **Atualmente é um stub**: todas as leituras retornam `0.0`. A leitura real de sensores no Windows é feita em C# pelo `HardwareMonitorService` (LibreHardwareMonitor); a DLL é opcional e o app funciona sem ela.

## Contrato nativo (ABI)

Definido em `src/RivaSensorLib/RivaSensorLib.h` e espelhado em `src/RivaMonitor/Native/SensorBridge.cs`. **Os dois lados precisam permanecer idênticos.**

### `NativeSensorSnapshot` (16 × `double` = 128 bytes, sem padding)

`CpuUsage, CpuTemp, CpuClock, CpuVoltage, CpuPower, GpuUsage, GpuTemp, GpuCoreClock, GpuMemoryClock, GpuVoltage, GpuMemoryUsed, GpuMemoryTotal, GpuPower, RamUsage, RamTotal, FanSpeedRpm`

- No C++, um `static_assert` garante `sizeof == 128`.
- No C#, `SensorBridge.TryLoad()` valida `Marshal.SizeOf<NativeSensorSnapshot>() == 128` antes de usar a DLL (evita estouro de buffer se os layouts divergirem).
- O enum `NativeSensorType` segue a mesma ordem dos campos (índices 0–15) usada por `RivaSensor_GetSensorName` / `RivaSensor_GetSensorValue`.

### Funções exportadas (`extern "C"`, cdecl)

| Função | Descrição |
|---|---|
| `RivaSensor_Init()` | Inicializa a biblioteca (retorna 0 em sucesso) |
| `RivaSensor_ReadSnapshot(NativeSensorSnapshot*)` | Zera e preenche o snapshot (stub) |
| `RivaSensor_GetSensorCount()` | Retorna 16 |
| `RivaSensor_GetSensorName(index, buffer, size)` | Nome do sensor (valida índice e buffer) |
| `RivaSensor_GetSensorValue(index, double*)` | Valor do sensor (valida índice) |
| `RivaSensor_Shutdown()` | Finaliza a biblioteca |

### API C# (`SensorBridge`)

`TryLoad()`, `ReadSnapshot()`, `GetSensorName(int)`, `Unload()` e `IsAvailable`. `TryLoad` captura `DllNotFoundException`, `EntryPointNotFoundException` e `BadImageFormatException`.

## Build

O target `BuildRivaSensorLib` do `RivaMonitor.csproj` compila a DLL antes do build, **apenas se o `.cpp`/`.h` forem mais novos que a DLL** (build incremental):

1. Localiza `vcvars64.bat` (VS 2022 BuildTools, Community, Professional ou Enterprise). Pode ser informado manualmente: `dotnet build -p:VcVarsPath="C:\...\vcvars64.bat"`.
2. Executa `call vcvars64.bat` + `cl /LD /EHsc /std:c++17 /DRIVASENSORLIB_EXPORTS`. O `RIVASENSORLIB_EXPORTS` é obrigatório para o header usar `dllexport`.
3. Copia `RivaSensorLib.dll` para o diretório de saída.

Se o MSVC não for encontrado ou o `cl` falhar, o build emite **aviso** (não erro) e o app segue sem a DLL. Alternativa: `CMakeLists.txt` em `src/RivaSensorLib/`.

## Próximos passos

- Implementar leitura real (NVML para GPU NVIDIA, ACPI/WMI direto etc.)
- Hook DirectX (`IDXGISwapChain::Present`) para captura de FPS independente
