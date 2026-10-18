# AppConfig / Config

Sistema de configuração baseado em JSON. O arquivo `config.json` fica na raiz do projeto e é carregado automaticamente.

## Modelos

### AppConfig
- `updateIntervalMs` - Intervalo de atualização em milissegundos (padrão: 1000)
- `logToCsv` - Habilitar logging em CSV
- `logToJson` - Habilitar logging em JSON
- `showOverlay` - Mostrar overlay na inicialização
- `overlayOpacity` - Opacidade do overlay (0-1)
- `overlayPosition` - Posição inicial do overlay (X, Y)
- `Overlay` - Configurações específicas do overlay
- `Sensors` - Quais sensores estão habilitados

### OverlayConfig
- `width` - Largura do overlay em pixels
- `showCpu/Gpu/Ram/Disk/Network/Fans/Fps` - Visibilidade de cada seção
- `showGpuProcesses` - Mostrar processos da GPU no overlay
- `compactMode` - Modo compacto
- `accentColor` - Cor de destaque (hex, ex: "#1E90FF")

### SensorConfig
- `Cpu/Gpu/Ram/Disk/Network/Motherboard` - Sensores habilitados

## Carregamento e localização

O caminho é resolvido por `ConfigPaths.ConfigFile` (independente da pasta de saída do build):

1. **Desenvolvimento:** sobe a partir do executável até encontrar `PhantomMonitor.slnx` (ou `.sln`) e usa o `config.json` da raiz do repositório.
2. **Publicado/instalado:** usa `%AppData%\PhantomMonitor\config.json`.

Se o arquivo não existir, `AppConfig.Load` copia o `config.example.json` (da mesma pasta ou ao lado do executável — o `.csproj` o copia para a saída) e cria o diretório se necessário.

## Validação e persistência

- `AppConfig.Normalize()` corrige valores inválidos (intervalo mínimo de 100 ms, opacidade entre 0,1 e 1, objetos `null`).
- `AppConfig.Save()` grava de forma atômica (arquivo `.tmp` + substituição) e retorna `bool`; o Editor de Overlay avisa se não conseguiu salvar.
- Falhas de leitura/gravação são registradas em `logs/errors.log` (ver [logging.md](logging.md)).
- `showOverlay` define o estado inicial do toggle "Overlay".
- `sensors.*`, `overlay.showNetwork`, `showGpuProcesses`, `showFans`, `compactMode`, `accentColor` e `fontSize` estão reservados no modelo, mas ainda não são consumidos pela interface.
