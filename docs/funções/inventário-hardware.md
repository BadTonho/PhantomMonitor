# Inventário de Hardware

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
| Unidade | Unidade canônica da categoria (ver `SensorUnits`) |
| Canônica | ✓ quando for série de índice 0, compatível com painel/alertas legados |

Mais 2 s de cadência de atualização (DispatcherTimer local, sem tocar no serviço).

## Aba "Hardware detectado"

Árvore dos nós enumerados (placa-mãe → SuperIO/EC, CPU, GPUs com sub-hardware...),
decodificada das linhas de diagnóstico `[HWR]` que o `HardwareMonitorService` já
produz por ciclo (mesma fonte da janela de debug) — sem duplicar enumerações.
A reconstrução da árvore ocorre só quando a estrutura muda.

Fonte: `Services/InventoryBuilder.cs` (`BuildSeries`/`BuildHardware`).
