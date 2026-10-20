# Roadmap — Paridade de Monitoramento (suítes de referência do mercado)

> O que fazer para o Phantom Monitor alcançar as suítes de referência do mercado.
> Gerado a partir da comparação com o código em fim de 2026 (pós a bateria de correções
> `79913ed..6233267`). **Status: P1–P4 implementados/entregues em
> `6b5c148..` (série "paridade"); P5 é decisão de escopo, não tarefa.
> Rodada 3 (`bb8342f..` = P8): os 4 gaps "Camada A" da análise RTSS×HWiNFO
> (sensores por fórmula, WHEA, journal JSON, EDID) também fechados.**

---

## O que JÁ é paridade hoje (não refazer)

- Frametime **por evento**: ETW/PresentMon (Windows) + hook próprio `vkQueuePresentKHR` + SHM por PID (Linux).
- 1% / 0.1% Low, min/max/avg, gráfico circular de 600 amostras.
- Alertas acionáveis (popup, som, script externo, desligamento com trava) — incluindo alvos por instância (`sensorIndex`).
- Overlay configurável (editor visual, presets, hotkey, tray), instância única.
- Export CSV/JSON + `errors.log`; sem injeção → sem risco de ban.
- Camada Vulkan implícita com gating por env (Linux).

---

## P1 — Framework de sensores dinâmico (séries por instância, estilo inventário completo) — ✅ CONCLUÍDO (`6b5c148`, `a6cbac5`, `9ee36fb`, `f6567a9`, `df6db76`)

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

## P6 — Paridade instrumental (estatísticas/OSD/telemetria) — ✅ CONCLUÍDO (2026-10-19)

Sete sub-etapas, um commit Conventional cada, build 0 erros/0 avisos, docs sincronizadas:

- **P6.1** Estatísticas por série (min/max/média da sessão) + reset (bandeja/inventário) + marcador `#marker` no CSV — `0acb085`
- **P6.2** `profiles[].visible: false` esconde o OSD por aplicativo — `60c72e5`
- **P6.3** Exportador HTTP do SDK (`GET /values` `/health`, loopback padrão, `listenAll` opt-in; UDP de propósito fora — `1caf566`
- **P6.4** Família `Battery*` (nível/%, tensão, potência com sinal, desgaste, AC) via WMI no Windows e `/sys/class/power_supply` no Linux; desktop sem bateria não publica nada — `5095a11`
- **P6.5** Métricas extras do PresentMon (GPU Busy / CPU Busy / CPU Wait / latência de present) + percentis puros P1/P99 de FPS e frametime, parse tolerante a versão — `159a0cf`
- **P6.6** Templating do OSD: linhas personalizadas com placeholders `{Chave}`/`{Chave:N}`/wildcard, ocultas até resolverem, sempre no fim do painel; cor só via config — `df4b72e`
- **P6.7** Pausa/retomada do monitoramento (bandeja + 2º atalho global `Ctrl+Alt+P`): ciclos pulados, telemetria congelada no último snapshot, estado não persistente — `5c54f8f`

A tabela da rodada 2 abaixo continua válida como registro dos gaps avaliados;
as linhas fechadas por P6/P8 estão marcadas ✅ na coluna "Ação definida".

## Rodada 3 — P8: gaps "Camada A" da análise RTSS×HWiNFO (2026-10-20) — ✅ CONCLUÍDO

Quatro commits Conventional, build 0 erros/0 avisos e engine validada por harness
(parser + WireUp de `SensorData`), documentação sincronizada por commit:

- **P8.1** Sensores customizados por fórmula (`customSensors` → série `Custom#i`
  com AST compilada; min/max/abs, referências por chave/wildcard/nome; avaliada
  antes do commit das estatísticas em Windows e Linux; template do OSD ganha
  fallback por rótulo) — `14ab0d3`
- **P8.2** Contador WHEA (`WheaErrorCount`: provedor `Microsoft-Windows-WHEA-Logger`
  no log System desde o início da sessão; reversa com early-break + cache 10 s;
  Windows apenas) — `b8433f3`
- **P8.3** Journal JSON contínuo (`logToJsonJournal` → JSONL com meta/sample &
  notas "início da sessão"/"reset" — mesma política append-only do CSV) — `a72c0fb`
- **P8.4** Identidade dos monitores via EDID/WMI (aba "Monitores" do inventário +
  `monitors` no snapshot JSON; PnP/Modelo/Série/Diagonal/Fabricação/Primário) — `bb8342f`

## Deliberadamente NÃO planejado (decisões já tomadas)

- Seqlock na SHM Vulkan (custo de 4 bytes/ABI por consistência total) —
  leitura eventual documentada; o leitor filtra valores plausíveis.
- Placeholders ("--°C/-fps") até o primeiro dado: cosmético, mantido.
- 1%/0.1% Low com <100 amostras ≈ mínimo: é a definição matemática; não mudar.

---

# Rodada 2 — gaps restantes (pós P1..P5)

> Nova verificação após a implementação de P1..P5. Os itens estão separados em
> **(A)** produto — dá para implementar no escopo atual;
> **(B)** política — fechados conscientemente por não injetar/tocar no processo do jogo;
> **(C)** engine — dependem de fontes de leitura que a LibreHardwareMonitor não abre
> (drivers próprios, MSR, NPU ou APIs proprietárias de vendor — NVAPI/ADLX).
>
> **Fontes (2026):** changelogs e páginas oficiais das suítes de referência
> de monitoramento/overlay (2024–2026).

## Onde ficamos atrás — overlay de jogos (padrão de mercado)

| Recurso | Camada | Ação definida |
|---|---|---|
| OSD in-frame (game capture, replay, gravação) | B | Fora por política; caminho seguro documentado: plugin OBS consumindo a SDK (docs/osd-in-frame-estudo.md) |
| Frame limiter (FRTC, millisecond) | B | Fora por política: exige hook no processo do jogo; não reabrir |
| Curva de ventoinha, V/F curve, OC scanner | B | P5: fora do escopo read-only (app separado um dia) |
| Captura de tela (BMP/PNG/JPG) e gravação de vídeo em tempo real (MJPG/VFW externos, NVENC/VCE/QuickSync) | B | Feita por hook no processo do jogo (padrão dos hooks de captura); OBS no plugin cobre gravação |
| Screenshot in-frame via hotkey | B | Não há acesso ao backbuffer sem hook; OBS cobre |
| **Métricas ricas do PresentMon** (GPU Busy, CPU Busy/CPU Wait, click-to-photon, percentis 1st/99th, modo de present) | A | ✅ Fechado pelo **P6.5** (GPU/CPU Busy, CPU Wait, latência de present e percentis P1/P99 de FPS/frametime; modo de present/click-to-photon não expostos pelo executável — não planejado) |
| **Latency markers NVIDIA Reflex** (`reflexlatency()/timespan()`, marcadores input→gpurender) | B | Implementado com injection/hook no jogo pelas suítes de referência — fora por política |
| **OverlayEditor / hypertext** — tags (`<B>`, `<FNT>`), gráficos de barra/timeband, fórmulas e filtros (`buf()`, condições), camadas condicionais, macros `%CPU%`, text tables | A | 🟡 Parcial: **P6.6** (templates `{Chave}`/`{Chave:N}` + cor) cobre texto/condição simples; gráficos embutidos e expressões = P7 de UI |
| Per-app "OSD ocultado" (jogos que não querem ver overlay) | A | ✅ Fechado pelo **P6.2** (`profiles[].visible: false`) |
| LCDs externos (Logitech, OLED/StreamDeck via plugins/ecossistema) | A-parcial | Nossa SDK (P2) dá os dados; escritores/apps externos fora do produto |
| Monitor remoto / 2ª máquina / painel na LAN (servidor de sensor remoto nas suítes de referência) | A | ✅ Fechado pelo **P6.3** (exportador HTTP `GET /values`, loopback/LAN opt-in; UDP de propósito fora) |
| Overlay VR (OpenVR overlay) | A (nicho) | Nicho; não planejado |

## Onde ficamos atrás — suítes de monitoramento de hardware

| Recurso | Camada | Ação definida |
|---|---|---|
| Colunas Min/Máx/Média/Atual por sensor + hotkey de reset | A | ✅ Fechado pelo **P6.1** (SeriesStatsTracker por snapshot + "Resetar estatísticas" na bandeja/colunas Min/Máx/Média no inventário) |
| Hotkey para **pausar/retomar** o monitoramento | A | ✅ Fechado pelo **P6.7** (tray com check + 2º atalho global; telemetria congelada no último snapshot) |
| Percentis 1st/99th de FPS e frametime (percentis brutos sobre janela móvel) | A | ✅ Fechado pelo **P6.5** (FpsP1/FpsP99 + FrametimeP1/FrametimeP99 sobre a janela de ~60 s) |
| Marcadores/hotkeys de benchmark: start/stop de log e segmento com marcador | A | ✅ Fechado pelo **P6.1** + **P8.3** (`#marker,<ts>,<label>` no CSV a cada reset + notas `{"kind":"note"}` no journal JSONL) |
| Acesso remoto: servidor de sensor remoto (TCP na LAN) para 2ª máquina e dashboards | A | ✅ Fechado pelo **P6.3** (mesma estrutura do P2 exportada por HTTP; LAN opt-in com `listenAll`) |
| Bateria (laptop: carga %, desgaste, draw, AC) | A | ✅ Fechado pelo **P6.4** (WMI `root\WMI`+`Win32_Battery` no Windows; `/sys/class/power_supply` no Linux; desktop não publica) |
| Contadores de erros de hardware **WHEA** (incl. device PCIe com falha) | A (médio) | ✅ Fechado pelo **P8.2** (`WheaErrorCount`: provedor `Microsoft-Windows-WHEA-Logger` no log System desde o início da sessão, EventLogQuery reversa com early-break e cache de 10 s; Windows apenas) |
| Sensores **customizados pelo usuário** (fórmulas sobre outros sensores) | A (médio) | ✅ Fechado pelo **P8.1** (`customSensors` → engine `CustomSensorEngine` com AST compilada; série `Custom#i` entra em stats/CSV/SDK/alertas; templates do OSD resolvem por nome) |
| Log de sensores em **JSON/YAML** | A (menor) | ✅ Fechado pelo **P8.3** para JSON (journal JSONL contínuo com notas de sessão — `JsonJournalLogger` e `logToJsonJournal`); YAML não planejado (consumidores práticos preferem JSONL/jq) |
| OSD próprio da suíte de hardware: gráfico de histórico no OSD, cor de fundo, múltiplos valores por linha, eixo com min/max global | A (menor) | Nosso overlay (P1/P3) cobre texto/séries; gráfico embutido e cor de fundo = melhorias de UI |
| Alerts por e-mail/webhook e "destinos de log" extras | A | Diferencial menor; o script externo já permite enviar — decidir depois |
| Polling **multithread/assíncrono** de sensores | A (caveat) | LHM é sequencial por design; paralelizar entre hardwares exige testes de concorrência no driver |
| Identidade dos monitores (fabricante/modelo via EDID); chroma/HDR/VRR | A-parcial | ✅ Fechado pelo **P8.4** no EDID (aba "Monitores" no inventário: PnP/Modelo/Serial/diagonal/fabricação/primário via `WmiMonitorID`+`WmiMonitorBasicDisplayParams`, cache 60 s + snapshot JSON); flags HDR/VRR exigem descriptor completo (P7) |
| Polimento de monitoramento: pesquisa, ordenação custom, colapso de grupos, tooltips, destaque de mudanças | A (menor) | Inventário absorve aos poucos |
| Intervalo de polling **por sensor** + smoothing/filtro médio por item | A (menor) | Hoje cadência global única; item de UX de nicho |
| Tabela SMART atributo a atributo (realocados, erros de mídia etc.) | C | LHM sintetiza pouco; viria de SMART bruto via WMI/Win32 — maior esforço, P7 |
| GPU: Hot Spot / Memory Junction como **séries separadas** (LHM expõe p/ NVIDIA) e per-chip VRAM de GDDR6X/GDDR7 | A-parcial | NVIDIA: basta mapear; per-chip/AMD: P7 (engine) |
| GPU: "throttle reasons" e power-limit flags (NVAPI/AMD; "Performance Limiting Reasons") | C | NVAPI interop + AMD User Mode — esforço real por vendor (P7) |
| CPU: C-states residency, VID per-core, Effective Clock, DRAM R/W bandwidth, PL1/PL2 dinâmico, % thermal throttle | C | MSR/SMN próprios — LHM não expõe (documentar como limite de engine) |
| NPU dedicado (AMD/Intel/Qualcomm) | C | Sem fonte via LHM hoje |
| Inventário profundo: VBIOS/ASIC quality/ECID, cont. RT/Tensor, ReBAR, PCIe error counters, SPD/timings (CUDIMM), PCIe link por slot | C-parcial | EDID/SPD básicos via WMI são baratos; o resto precisa de NVAPI/SetupAPI (P7) |
| Ecossistemas de terceiros: PSUs (Corsair/MSI/NZXT), controladores de fan, WireView, ARM64/Snapdragon | C | Amplitude de leitura fora da LHM não disponível |

## Recomendação (ordem de valor/esforço)

1. ✅ **P6.1** Min/max/avg por série + **reset de estatísticas** (tray/inventário) com marcadores no CSV — feito (`0acb085`)
2. ✅ **P6.2** `profiles[].visible` (esconder OSD por app) — feito (`60c72e5`)
3. ✅ **P6.3** Exportador HTTP do SDK (loopback/LAN opt-in; UDP deliberadamente fora) — feito (`1caf566`)
4. ✅ **P6.4** Bateria via WMI/sysfs (Windows + Linux, módulo próprio da família `Battery*`) — feito (`5095a11`)
5. ✅ **P6.5** Métricas extras do PresentMon (GPU/CPU Busy, CPU Wait, latência de present) + percentis 1st/99th — feito (`159a0cf`)
6. ✅ **P6.6** OSD templating (linhas com placeholders, condições por resolução de séries) — feito (`df4b72e`)
7. ✅ **P6.7** Pausar/retomar monitoramento (tray/atalho global `Ctrl+Alt+P`) — feito (`5c54f8f`)

**P6 integralmente concluído.** Próximo capítulo natural (P7, esforço grande e/ou
dependente de engine tercerizada) permanece como "limitação de engine" documentada.

**Rodada 3 (P8, 2026-10-20):** os 4 gaps aproveitáveis da análise RTSS×HWiNFO
(sensores customizados, WHEA, journal JSON, EDID dos monitores) foram entregues
(`14ab0d3`, `b8433f3`, `a72c0fb`, `bb8342f`) — detalhados na seção "Rodada 3" acima.

**P7 (maiores, exigem NVAPI/AMD User Mode/MSR ou driver próprio): SMART completo, throttle
reasons, EDID/SPD/PCIe detalhes, C-states/NPU — cada um documentado como "limitação da
engine" quando implementável apenas com driver próprio.**

## Onde o Phantom Monitor já está à frente

- **SDK de memória compartilhada gratuita e estável** (P2): em soluções comerciais
  de referência, o acesso de terceiros à telemetria bruta via memória compartilhada
  tende a ser recurso licenciado/pago — aqui é aberto, documentado e versionado.
- **Sem hook/injection**: anti-cheats podem restringir os OSDs que injetam hooks no
  processo do jogo — o nosso design nunca corre esse risco, por construção.
- **CSV livre e dinâmico** com séries por instância, JSON de snapshot local e leitor de
  referência da SDK incluso.
