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

---

# Rodada 2 — gaps restantes vs RTSS/Afterburner e HWiNFO (pós P1..P5)

> Nova verificação após a implementação de P1..P5. Os itens estão separados em
> **(A)** produto — dá para implementar no escopo atual;
> **(B)** política — fechados conscientemente por não injetar/tocar no processo do jogo;
> **(C)** engine — dependem de fontes de leitura que a LibreHardwareMonitor não abre
> (drivers próprios, MSR, NPU ou APIs proprietárias de vendor — NVAPI/ADLX).

## Onde ficamos atrás do RTSS / MSI Afterburner

| Recurso | Camada | Ação definida |
|---|---|---|
| OSD in-frame (game capture, replay, gravação) | B | Fora por política; caminho seguro documentado: plugin OBS consumindo a SDK (docs/osd-in-frame-estudo.md) |
| Frame limiter (FRTC, millisecond) | B | Fora por política: exige hook no processo do jogo; não reabrir |
| Curva de ventoinha, V/F curve, OC scanner (Afterburner) | B | P5: fora do escopo read-only (app separado um dia) |
| Captura de vídeo / Instant Replay | B | Mesma classe de hook do OSD in-frame; OBS no plugin cobre gravação |
| Screenshot in-frame via hotkey | B | Não há acesso ao backbuffer sem hook; OBS cobre |
| **Latency/Reflex markers no Windows** (colunas de "PC latency" do PresentMon v2) | A | **Gap real do produto**: hoje `FpsMonitor` só parsa `msBetweenPresents`/`FrameTime` → adicionar colunas de latência quando o PresentMon instalado as expor (novas séries) |
| **OSD com linhas definidas pelo usuário** (templates `{CpuTemperature}`, `{Fps}` formatáveis, cores por limiar, hotkey por elemento) | A | **Gap real**: RTSS tem grupos/hotkeys/cores condicionais; a nossa whitelist cobre o que aparece, mas não composição de linhas nem cores condicionais |
| Per-app "OSD ocultado" (jogos que não querem ver overlay) | A | **Gap pequeno**: `profiles` não tem flag de visibilidade → `visible: false` por perfil (quick win) |
| LCDs externos (Logitech, OLED/StreamDeck via plugins/ecossistema) | A-parcial | Nossa SDK (P2) dá os dados; escritores/apps externos fora do produto |
| Monitor remoto / 2ª máquina / painel na LAN (HWiNFO tem telesensor-server; RTSS usa HUDSight/3rd-party) | A | **Gap do produto**: SDK é só memória local → exportador HTTP/UDP (loopback/LAN opcional) |
| Overlay VR (OpenVR overlay) | A (nicho) | Nicho; não planejado |

## Onde ficamos atrás do HWiNFO

| Recurso | Camada | Ação definida |
|---|---|---|
| Colunas Min/Máx/Média/Atual por sensor + reset da sessão | A | **Gap real**: `SensorData` mantém só o valor atual (FPS tem estatística própria) → estatística por série (min/max/avg janela fixa ou sessão) + reset no inventário/tray |
| Marcadores/hotkeys de benchmark: start/stop de log e segmento com marcador | A | **Gap barato**: hotkey/tray "reset estatística" + linha `#marker` no CSV |
| Acesso remoto: "Remote Sensor Server" (TCP na LAN) para 2ª máquina e dashboards | A | Mesmo do item remoto do RTSS: exportador HTTP/UDP sobre a mesma estrutura do P2 |
| Bateria (laptop: carga %, desgaste, draw, AC) | A | **Gap real, barato** via WMI (`Win32_Battery` + `BatteryStaticData` em `root\WMI`) |
| Alerts por e-mail/webhook e "destinos de log" extras | A | Diferencial menor; o script externo já permite enviar — decidir depois |
| Tabela SMART atributo a atributo (realocados, erros de mídia etc.) | C | LHM sintetiza pouco; viria de SMART bruto via WMI/Win32 — maior esforço, P7 |
| GPU: "throttle reasons" (NVAPI)/(thermal slow, power limit, no-load) e ICX/VRAM temp por grupo | C | NVAPI interop (NVIDIA) + AMD User Mode API — esforço real e especificadores por vendor (P7) |
| CPU: C-states residency, VID per-core, PL1/PL2, contadores de throttle | C | Exige MSR próprio/driver — LHM não expõe; fora do escopo atual (documentar) |
| NPU usage (Intel MTL/Snapdragon) | C | Idem engine (sem fonte via LHM hoje) |
| Inventário profundo: VBIOS/EDID, SPD de RAM (timings), PCIe link por slot, resumo de sistema/OS | C-parcial | EDID (WMI MonitorID) e SPD (WMI SMBIOS MemoryDevice) são baratos; PCIe/VBIOS precisariam de NVAPI/SetupAPI (P7) |
| Intervalo de polling **por sensor** + smoothing/filtro médio por item | A-menor | Hoje cadência global única; item de UX de nicho (config por família talvez) |
| Grafos de histórico (sparklines) por sensor na janela de monitoramento | A (menor) | CSV/SDK já permitem; UI extra — depois |

## Recomendação (ordem de valor/esforço)

1. **P6.1** Min/max/avg por série + **reset de estatísticas** (tray/hotkey) com marcadores no CSV — "efeito HWiNFO" na leitura e nos dados.
2. **P6.2** `profiles[].visible` (esconder OSD por app) — o override de séries já existe.
3. **P6.3** Exportador HTTP/UDP do SDK (loopback/LAN) — painéis e 2ª máquina sem tocar no produto.
4. **P6.4** Bateria via WMI (módulo separado, valioso para laptops).
5. **P6.5** Latency markers do PresentMon v2 ("PC latency") quando disponíveis — conferir as colunas na versão do PresentMon usada pelo projeto.
6. **P6.6** OSD templating (linhas customizadas) — médio; último da fila.

P7 (maiores, exigem NVAPI/AMD User Mode/MSR ou driver próprio): SMART completo, throttle
reasons, EDID/SPD/PCIe detalhes, C-states/NPU — cada um documentado como "limitação da
engine" quando implementável apenas com driver próprio.
