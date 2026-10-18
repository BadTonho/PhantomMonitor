# HardwareMonitorService
 
Serviço de leitura de sensores de hardware para ambiente Windows, implementando a interface `ISensorService`. Utiliza a biblioteca LibreHardwareMonitor para acessar sensores de CPU, GPU, memória, disco, placa-mãe e rede, com fallbacks via WMI (Windows Management Instrumentation) quando necessário. No Linux, a `SensorServiceFactory` instancia automaticamente o `LinuxSensorService`.

## Funcionalidades

- Leitura de temperatura, uso, clock, voltagem e potência da CPU
- Leitura de temperatura, uso, clock e potência da GPU
- Leitura de uso e disponibilidade de RAM
- Leitura de uso e I/O de disco
- Leitura de velocidade de ventoinhas
- Fallback via WMI para temperatura da CPU (3 métodos diferentes)
- Fallback via Performance Counters para uso de RAM
- Leitura de processos da GPU via WMI

## Principais métodos

- `Start()` - Inicia o monitoramento dos sensores
- `Stop()` - Para o monitoramento
- `GetCurrentSnapshot()` - Retorna o snapshot mais recente dos sensores

## Eventos

- `SensorUpdated` - Disparado a cada ciclo de atualização com os dados mais recentes
