# Inventário de Hardware

> **Idioma:** Português (Brasil) · [English](../en/functions/hardware-inventory.md)

## Inventário no dashboard Avalonia

No dashboard Avalonia, **Inventário** é uma página da janela principal, acessível
pela barra lateral. Ela acompanha os snapshots do serviço ativo e agrupa as séries
em CPU, GPU, memória, disco, rede, ventoinhas, bateria e FPS. Cada categoria é um
grupo expansível, aberto por padrão, com uma tabela compacta: Sensor, Atual,
Mínimo, Máximo e Média. O nome amigável da origem é exibido como sensor; a chave
técnica (por exemplo, `CpuPerCore#1`) fica no tooltip. A unidade acompanha o valor
atual e as estatísticas de sessão. A tabela e seus grupos ocupam toda a largura
disponível, mantendo as colunas alinhadas até a margem direita. Cada família
com instâncias mostra uma linha-resumo. Se houver uma única instância, a
sublinha é omitida e seu nome amigável aparece no tooltip do resumo após dois
segundos de hover. Com duas ou mais instâncias, as sublinhas indentadas ficam
visíveis; uma seta no resumo recolhe ou expande apenas essas instâncias, sem
ocultar o resumo ou suas métricas. A regra vale em todas as categorias. CPU Total mantém
a linha do total e lista os núcleos abaixo; se não houver um agregado
publicado, a linha-resumo usa a média das instâncias compatíveis. Fórmulas
personalizadas e sensores sem instâncias continuam como linhas próprias.
As médias calculadas têm mínimo, máximo e média de sessão em memória, limpos por
**Resetar estatísticas**. Sem amostra, as estatísticas mostram `--` (NaN).

A página também tem, desde a F1.4, os acréscimos do InventoryWindow do WPF:

- **Botão "Resetar estatísticas"** no cabeçalho: zera min/máx/média de todas as
  séries (`ISensorService.ResetSeriesStats`) e grava um marcador `#marker` no CSV
  ativo + nota equivalente no journal JSONL (`reset (inventário)`), com
  confirmação transitória junto do botão e amostras zeradas na hora.
- **Card "Hardware detectado"**: árvore dos nós enumerados (placa-mãe →
  SuperIO/EC, CPU, GPUs com sub-hardware...), decodificada das linhas
  `[HWR]` via `InventoryBuilder.BuildHardware` (Core); nós expandidos por
  padrão e reconstrução da árvore **só quando a enumeração muda** (assinatura
  `Depth:Type:Name`). No Windows sem administrador ou sem detecção, um aviso
  orienta iniciar como Administrador.
- **Card "Monitores (EDID)"**: instância WMI (device), fabricante, modelo,
  código, serial, diagonal e fabricação + indicador PRIMÁRIO (mesma fonte
  `WmiMonitorID`/`WmiMonitorBasicDisplayParams`); a lista só troca quando o
  conjunto de monitores muda. Sem monitor reportado (desktops/VMs/RDP), o card
  fica oculto e o snapshot JSON publica `monitors: []`. Se a consulta WMI falhar,
  o app segue com a lista vazia e registra uma única ocorrência diagnóstica por
  execução; uma consulta válida sem monitores não gera erro.
- O resumo do topo mostra `N nós de hardware · M monitor(es)` quando existirem.

Quando o serviço não publica nenhum valor de um tipo esperado, o inventário
inclui uma linha **Indisponível** para esse tipo (colunas de sessão em `--`).
A lista considera somente as categorias que o coletor atual expõe; sensores
exclusivos do Windows não são tratados como ausentes no Linux. Um valor
publicado igual a zero continua sendo uma leitura disponível. Por exemplo,
bateria AC desconectada aparece como "Desconectada", em vez de ser confundida
com falta de leitura.

O inventário atualiza junto com cada snapshot. Enquanto aguarda a primeira
leitura, mostra um estado de espera; se a coleta for pausada ou parada, mantém
os últimos valores recebidos e as estatísticas da sessão até o reset. A página
é somente de consulta.

A renderização é estável entre snapshots: os grupos fixos (CPU, GPU, memória,
disco, rede, ventoinhas, bateria e FPS) e as linhas continuam sendo as mesmas
instâncias, e cada leitura é reconciliada por chave — apenas a string que mudou
re-renderiza. O estado aberto/recolhido dos grupos é mantido durante a execução.
O grupo **Outros**, com séries fora das categorias fixas, entra e sai do fim da
lista conforme o que o hardware publica. Fonte:
`ViewModels/SensorInventoryBuilder.cs` (plano declarativo com Min/Max/Avg),
`ViewModels/MainWindowViewModel.Inventory.cs` (árvore, monitores, reset e
reconciliação dos grupos e linhas do inventário).

## Detalhes internos do inventário (herdados da janela do WPF)

O frontend WPF — aposentado na Fase 2 — tinha uma janela de inventário separada
com as abas abaixo; a página **Inventário** do app único compartilha a MESMA
fonte Core (`InventoryBuilder`, `SeriesStats`, `MonitorInfo`) e cobre as mesmas
informações em grupos de sensores expansíveis e seções de hardware/monitores
(resumo acima). As abas ficam como referência dos dados e
dos marcadores de reset.

Janela que listava **o que o monitor enxerga do hardware** em tempo real. Servia para
duas tarefas práticas: diagnosticar por que um sensor não aparece (ex.: falta de
administrador) e descobrir **chaves de série** para usar em alertas
(`sensorIndex`) ou na whitelist do OSD (`overlay.shownSeries`).

## Acesso (janela histórica do WPF)

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

Fonte: `PhantomMonitor.Core/Services/Inventory/InventoryBuilder.cs` (`BuildSeries`/`BuildHardware`),
`HardwareMonitorService.GetMonitors` e `Models/Inventory/MonitorInfo.cs`.
