# RivaSensorLib (C++ DLL) / SensorBridge

Projeto de DLL nativa em C++ para leitura de sensores de hardware com performance máxima. Atualmente é um stub (esqueleto) pronto para ser implementado.

## SensorBridge.cs

P/Invoke definitions para chamar as funções da DLL nativa do C#:

- `GetSnapshot()` - Retorna um snapshot de todos os sensores (estrutura `SensorSnapshot`)
- `GetLastError()` - Retorna a última mensagem de erro

## RivaSensorLib (C++)

### Estruturas
- `SensorSnapshot` - Contém valores de CPU (uso, temperatura), GPU (uso, temperatura), RAM (uso, disponível)

### Funções
- `GetSnapshot(SensorSnapshot* snap)` - Preenche um snapshot com leituras dos sensores
- `GetLastError()` - Retorna mensagem de erro (sem implementação atual)

## Build

A DLL é compilada automaticamente como parte do build do projeto WPF via target pre-build no `.csproj`:

1. Executa `vcvars64.bat` para configurar o ambiente MSVC
2. Compila com `cl /LD /EHsc`
3. Copia a DLL para o diretório de output

## Próximos passos

- Implementar leitura real via WinRing0 para acesso direto a sensores
- Implementar hook DirectX (IDXGISwapChain::Present) para captura de FPS independente
- NVML para leitura de GPU NVIDIA
- ACPI/WMI direto para leitura de temperatura
