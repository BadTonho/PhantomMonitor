# Roadmap — Paridade de Monitoramento (RTSS/Afterburner · HWiNFO)

> O que fazer para o Phantom Monitor chegar perto dos rivais. Gerado a partir da
> comparação com o código em fim de 2026 (pós a bateria de correções
> `79913ed..6233267`). **Status: P1–P4 implementados/entregues em
> `6b5c148..` (série "paridade"); P5 é decisão de escopo, não tarefa.**

---

## O que JÁ é paridade hoje (não refazer)

- Frametime **por evento**: ETW/PresentMon (Windows) + hook próprio `vkQueuePresentKHR` + SHM por PID (Linux).
- 1% / 0.1% Low, min/max/avg, gráfico circular de 600 amostras.
- Alertas acionáveis (popup, som, script externo, desligamento com trava) — incluindo alvos por instância (`sensorIndex`).
- Overlay configurável (editor visual, presets, hotkey, tray), instância única.
- Export CSV/JSON + `errors.log`; sem injeção → sem risco de ban.
- Camada Vulkan implícita com gating por env (Linux).

---

## P1 — Framework de sensores dinâmico (efeito "HWiNFO") — ✅ CONCLUÍDO (`6b5c148`, `a6cbac5`, `9ee36fb`, `f6567a9`, `df6db76`)

> Fundação: `SensorKey` ("Categoria#índice") + `SensorData.Series/SeriesLabels`.
> O LibreHardwareMonitor **já entregava** os valores; o trabalho foi de modelagem
> na coleta (Windows e Linux), exibição e exportação.

- [x] Multi-GPU: séries `GpuCore#i`, `GpuTemperature#i`, VRAM etc. por card
      (dedicada = GPU 1; canônicas de índice 0 preservadas).
- [x] CPU real por núcleo: `CpuPerCore#i`, `CpuTemperature#i`, `CpuClock#i`
      (índice derivado do próprio sensor; canônicas continuam max/preferido).
- [x] Ventoinhas N slots: `FanSpeed#i` por slot (placa-mãe/SuperIO/EC/hwmon).
- [x] Disco por drive: `DiskRead/Write/Usage/Temperature/Health/TotalRead/TotalWritten#i`
      com rótulo do disco (NVMe SMART: saúde via "Remaining Life", endurance "Data Read/Written").
- [x] VRM e rails: `VrmTemperature#i` (VRM/VSoC/VCore), `RailVoltage#i` (+12V/+5V/VCore...),
      `MotherboardTemp#i` por sensor, `RamTemperature#i` (Windows); EC via mesmo caminho.
- [x] Energia: `CpuPower#i` por domínio (IA/GT/...); canônicas `CpuPower`/`GpuPower` mantidas.
- [x] Rede por interface: `NetworkUp/Down#i` + totais da sessão `NetworkUpTotal/DownTotal` (Windows e Linux).
- [x] `HardwareInventory`: [Janela de Inventário](funções/inventário-hardware.md)
      (séries publicadas + árvore de hardware) na sidebar.
- [x] Overlay: linhas por série com whitelist configurável (`overlay.shownSeries`,
      editor com checkboxes) — abordagem "por família+wildcard" no lugar de checkbox
      individual por sensor; ordem por família e teto de 24 linhas.
- [x] Linux no mesmo modelo: per-core (`/proc/stat`), por-disco (`/proc/diskstats`),
      por-NIC (`/proc/net/dev`), multi-card DRM e slots de hwmon.

## P2 — SDK de telemetria via memória compartilhada — ✅ CONCLUÍDO (`c7b42a2`)

- [x] `MemoryMappedFile` versionada `phantom_monitor_telemetry_v1` (header
      magic `PMTS`/version/seq/count/timestamp) publicada a cada ciclo do monitoramento.
- [x] Tabela de 256 entradas fixas (key[40] | double | label[32]) com o mesmo
      `SensorKey` do CSV/JSON; leitor de referência em `tools/TelemetrySdkReader/`.
- [x] Consistência por **seqlock** (seq ímpar/par); leitor repete leitura quando seq muda.
- [x] `sensors_latest.json` mantido como fallback legado (deprecando devagar).
- [x] Documento `docs/telemetry-sdk.md` com layout byte a byte, protocolo C/C# e limites
      — link no README.

## P3 — Per-app profiles do OSD — ✅ CONCLUÍDO (`435d0b8`, `f3f3d82`)

- [x] Config `profiles: [{ name, appMatch, offsetX?, offsetY?, width?, fontSize?,
      opacity?, series?, accentColor? }]` — herda o global para campos vazios.
- [x] Matching por executável em foco a cada tick de posição (cache de nomes;
      `""`/`"*"` = universal).
- [x] UI: cartões de perfil no Overlay Editor (nome, AppMatch com "Capturar app
      em foco", offsets, largura, fonte, opacidade, whitelist de séries, acento).
- [x] Persistência em `config.json` + `Config.Normalize` com validação.
- [x] Tray: seção dinâmica "Automático (por aplicativo)" + perfis (fixar manualmente);
      indicador `◉ perfil: X` no rodapé do OSD quando ativo.

## P4 — OSD in-frame no Windows — ✅ ENTREGUE COMO ESTUDO + POLÍTICA (`docs/osd-in-frame-estudo.md`)

> Decisão: **não implementar hooks/injeção**. O caminho seguro registrado no
> estudo é um plugin OBS consumindo a SDK de telemetria (P2) e renderizando o
> OSD dentro do pipeline de captura do OBS — in-frame para game capture, zero
> risco de anti-cheat. Nenhuma DLL nativa, nenhum schema de config morto.

- [x] Estudo de arquitetura completo com alternativas avaliadas e esforço
      (`docs/osd-in-frame-estudo.md`); caminho "DLL injetada que intercepta Present"
      **explicitamente descartado** (assinatura clássica de cheat).
- [x] Comunicação pelo padrão SHM já existente (P2) — é a base do design recomendado.
- [x] Nota de risco anti-cheat no README ("Políticas por design"); o overlay WPF
      segue como default de risco zero.

## P5 — Controle (OC/ventoinha/limiter) — 🔒 DECISÃO: FORA DO ESCOPO

> Depende de escrita em hardware (driver de kernel/via NVAPI/ADLX). Não
> recomendado dentro do mesmo processo do monitor — se um dia entrar, é um
> app/pacote separado.

- [x] (decisão explícita de NÃO fazer no curto prazo; mantém o escopo "read-only")

## Deliberadamente NÃO planejado (decisões já tomadas)

- Seqlock na SHM Vulkan (custo de 4 bytes/ABI por consistência total) —
  leitura eventual documentada; o leitor filtra valores plausíveis.
- Placeholders ("--°C/-fps") até o primeiro dado: cosmético, mantido.
- 1%/0.1% Low com <100 amostras ≈ mínimo: é a definição matemática; não mudar.
