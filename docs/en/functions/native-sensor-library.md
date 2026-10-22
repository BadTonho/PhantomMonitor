> **Language:** English · [Português (Brasil)](../../funções/sensor-nativo.md)

# PhantomSensorLib (native C++ library)

`src/PhantomSensorLib/` currently contains a stub with no active application pipeline. Sensor reads return `0.0`; no runtime consumer loads the library. Windows sensor readings are currently handled in C# by `HardwareMonitorService` and LibreHardwareMonitor.

The source and `CMakeLists.txt` remain as a basis for possible future native work. They are not built or copied by the WPF build; the unused `BuildPhantomSensorLib` target and C# bridge were removed.

## Native ABI reference

The contract is defined in `PhantomSensorLib.h`, and any future C# consumer must mirror it exactly.

`NativeSensorSnapshot` contains 16 doubles (128 bytes): CPU usage, temperature, clock, voltage, and power; GPU usage, temperature, core/memory clocks, voltage, memory used/total, and power; RAM usage/total; and fan RPM. C++ verifies the size with `static_assert`; a future C# interop should verify `Marshal.SizeOf` is 128 before reading.

The `NativeSensorType` enum follows the same field order, indices 0–15. The C ABI exports initialization, snapshot reading, sensor count/name/value accessors, and shutdown. Accessors validate indices and buffer pointers.

The exported functions are `PhantomSensor_Init()` (zero indicates success), `PhantomSensor_ReadSnapshot(NativeSensorSnapshot*)` (currently clears/fills the stub snapshot), `PhantomSensor_GetSensorCount()` (16), `PhantomSensor_GetSensorName(index, buffer, size)`, `PhantomSensor_GetSensorValue(index, double*)`, and `PhantomSensor_Shutdown()`. They use `extern "C"` with the cdecl calling convention.

## Manual build reference

If the library is activated in future, build with Visual Studio 2022 `vcvars64.bat` and `cl /LD /EHsc /std:c++17 /DPHANTOMSENSORLIB_EXPORTS`, or use the CMake project. Real sensor reading (for example, through vendor APIs or direct platform interfaces) must be implemented before reconnecting a consumer.

The `PHANTOMSENSORLIB_EXPORTS` define is required for the header's `dllexport` declarations. A future interop should reject a mismatched struct size before touching the buffer to prevent memory corruption if either side changes its ABI.
