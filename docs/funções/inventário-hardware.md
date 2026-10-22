# Inventário de Hardware

> **Idioma:** Português (Brasil) · [English](../en/functions/hardware-inventory.md)

## Inventário no dashboard Avalonia (Linux)

No frontend Linux, **Inventário** é uma página da janela principal, acessível pela
barra lateral. Ela acompanha os snapshots do serviço Linux e agrupa as séries em
CPU, GPU, memória, disco, rede, ventoinhas, bateria e FPS. Cada linha mostra a
chave (`CpuPerCore#1`), o rótulo da origem, o valor atual e a unidade. Séries por
núcleo, dispositivo, interface e demais instâncias são exibidas individualmente.

Quando o serviço não publica nenhum valor de um tipo Linux esperado, o inventário
inclui uma linha **Indisponível** para esse tipo. A lista considera somente as
categorias que o coletor Linux atual expõe; sensores exclusivos do Windows não
são tratados como ausentes no Linux. Um valor publicado igual a zero continua
sendo uma leitura disponível. Por exemplo, bateria AC desconectada aparece como
“Desconectada”, em vez de ser confundida com falta de leitura.

O inventário atualiza junto com cada snapshot. Enquanto aguarda a primeira
leitura, mostra um estado de espera; se a coleta for pausada ou parada, mantém
os últimos valores recebidos. A página é somente de consulta.

## Inventário WPF (Windows)

O restante desta página descreve a janela de inventário do frontend WPF para
Windows. Seus recursos de estatísticas de sessão, hardware enumerado e monitores
EDID são específicos desse frontend e não fazem parte do inventário Avalonia Linux.

Janela que lista **o que o monitor enxerga do hardware** em tempo real. Serve para
duas tarefas práticas: diagnosticar por que um sensor não aparece (ex.: falta de
administrador) e descobrir **chaves de série** para usar em alertas
(`sensorIndex`) ou na whitelist do OSD (`overlay.shownSeries`).

## Acesso

- Sidebar → "🔎 Inventário de Hardware" (nova janela não modal).
- **Não exige** que o monitor esteja monitorando; sem snapshot ativo a janela mostra
  o aviso "Sem dados — inicie o monitoramento."

## Aba "Séries publicadas"

Grade com todas as séries do snapshot atual:

| Coluna | Descrição |
|---|---|
| Série | Chave completa: `GpuCore` (canônica) ou `GpuCore#1` (instância) |
| Sensor de origem | Rótulo do sensor/hardware de onde o valor veio |
| Valor | Formatado com unidade (`45°C`, `3.2 GB/s`, `1850 MHz`) |
| Mín (sessão) / Máx (sessão) / Média | Estatísticas acumuladas desde o último "Resetar estatísticas"; `--` até haver amostra |
| Unidade | Unidade canônica da categoria (ver `SensorUnits`) |
| Canônica | ✓ quando for série de índice 0, compatível com painel/alertas legados |

Mais 2 s de cadência de atualização (DispatcherTimer local, sem tocar no serviço).

### Botão "Resetar estatísticas"

No topo da janela: zera min/máx/média de todas as séries (mesma ação do item da
bandeja) e grava um marcador `#marker,reset (inventário)` no CSV ativo (+ nota
equivalente no journal JSONL ativo) — o segmento anterior fica delimitado na
planilha.

## Aba "Hardware detectado"

Árvore dos nós enumerados (placa-mãe → SuperIO/EC, CPU, GPUs com sub-hardware...),
decodificada das linhas de diagnóstico `[HWR]` que o `HardwareMonitorService` já
produz por ciclo (mesma fonte da janela de debug) — sem duplicar enumerações.
A reconstrução da árvore ocorre só quando a estrutura muda.

## Aba "Monitores" (identidade via EDID/WMI)

ID dos monitores conectados, decodificado de `root\wmi` (cache de **60 s**):

| Coluna | Fonte |
|---|---|
| Instância WMI (device) | `WmiMonitorID.InstanceName` (identifica o device; estável com múltiplos monitores) |
| Fabricante | `ManufacturerName` (código PnP do EDID: `DEL`, `SAM`, `LGD`... decodificado) |
| Modelo | `UserFriendlyName` (nome do painel: `U2723QE`) — sem nome no EDID cai no `ProductCodeID` |
| Código | `ProductCodeID` (texto do EDID ou decimal concatenado) |
| S/N | `SerialNumberID` (série do painel) |
| Diagonal | `WmiMonitorBasicDisplayParams`: √(largura² + altura²) cm → polegadas (`--` se não reportado) |
| Fabricação | `YearOfManufacture` + semana (`2023 s24`) |
| Primário | bit da propriedade `VideoInputType` |

- Somente monitores **ativos** (`Active ≠ 0`): entradas "fantasma" são removidas.
- Também vai para o snapshot JSON (`monitors`) e para qualquer consumidor do
  SDK via snapshot; o CSV e a tabela da SDK não mudam (são séries numéricas).
- Desktops/VMs/RDP sem monitor reportado: aba vazia (nenhuma falha é gerada).

Fonte: `Services/InventoryBuilder.cs` (`BuildSeries`/`BuildHardware`),
`HardwareMonitorService.GetMonitors` e `Models/MonitorInfo.cs`.
