# PhantomSensorLib (C++ DLL)

Fontes nativas em C++ para leitura de sensores, mantidas em `src/PhantomSensorLib/`. **Atualmente é um stub sem pipeline ativo no aplicativo**: todas as leituras retornam `0.0` e **nada consome a DLL em runtime** — a leitura real de sensores no Windows é feita em C# pelo `HardwareMonitorService` (LibreHardwareMonitor).

> Os fontes e o `CMakeLists.txt` permanecem no repositório como base para uma futura extensão nativa (ver "Próximos passos"), mas **não são mais compilados/copiados pelo build do WPF** — o target `BuildPhantomSensorLib` e o interop `SensorBridge.cs` foram removidos por serem código morto (DLL construída, copiada para a saída e nunca chamada).

## Contrato nativo (ABI) — referência futura

Definido em `src/PhantomSensorLib/PhantomSensorLib.h` (qualquer futuro consumidor C# precisa espelhar de forma idêntica). **Os dois lados precisam permanecer idênticos.**

### `NativeSensorSnapshot` (16 × `double` = 128 bytes, sem padding)

`CpuUsage, CpuTemp, CpuClock, CpuVoltage, CpuPower, GpuUsage, GpuTemp, GpuCoreClock, GpuMemoryClock, GpuVoltage, GpuMemoryUsed, GpuMemoryTotal, GpuPower, RamUsage, RamTotal, FanSpeedRpm`

- No C++, um `static_assert` garante `sizeof == 128`.
- Um futuro interop em C# deve validar `Marshal.SizeOf == 128` antes de usar a DLL (evita estouro de buffer se os layouts divergirem).
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

## Build (manual, se for ativado no futuro)

1. `vcvars64.bat` (VS 2022) + `cl /LD /EHsc /std:c++17 /DPHANTOMSENSORLIB_EXPORTS`. O `PHANTOMSENSORLIB_EXPORTS` é obrigatório para o header usar `dllexport`.
2. Alternativa: `CMakeLists.txt` em `src/PhantomSensorLib/`.

## Próximos passos

- Implementar leitura real (NVML para GPU NVIDIA, ACPI/WMI direto etc.) antes de religar algum consumidor
- Um novo interop C# deverá validar o tamanho da struct antes de usar (128 bytes)
