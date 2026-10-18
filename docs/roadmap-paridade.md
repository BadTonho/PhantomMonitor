# Roadmap — Paridade de Monitoramento (RTSS/Afterburner · HWiNFO)

> O que fazer para o Phantom Monitor chegar perto dos rivais. Gerado a partir da
> comparação com o código em fim de 2026 (pós a bateria de correções
> `79913ed..6233267`). Marcas `[ ]` são tarefas exequíveis; os números de
> prioridade são de valor/esforço, não de urgência rígida.

---

## O que JÁ é paridade hoje (não refazer)

- Frametime **por evento**: ETW/PresentMon (Windows) + hook próprio `vkQueuePresentKHR` + SHM por PID (Linux).
- 1% / 0.1% Low, min/max/avg, gráfico circular de 600 amostras.
- Alertas acionáveis (popup, som, script externo, desligamento com trava).
- Overlay configurável (editor visual, presets, hotkey, tray), instância única.
- Export CSV/JSON + `errors.log`; sem injeção → sem risco de ban.
- Camada Vulkan implícita com gating por env (Linux).

---

## P1 — Framework de sensores dinâmico (efeito "HWiNFO") — VALOR MÁXIMO, CUSTO BAIXO

> O LibreHardwareMonitor **já entrega** esses valores; o trabalho está no modelo de
> dados (o `SensorType` enum fixo de ~30 itens vira chave de série/instância) e na
> exibição (`OverlayWindow.AddMetricRow` e o painel de métricas do `MainWindow`).

- [ ] Multi-GPU: `Gpu0/Gpu1/…` (hoje o loop de `HardwareMonitorService` deixa a
      última sobrescrever — comentário nas linhas ~125: "a dedicada prevalece").
      = iGPU + dGPU visíveis ao mesmo tempo.
- [ ] CPU real por núcleo: temp/load/clock de cada core (hoje `CpuPerCore` é só o
      **max** — `PutMax`). Overlay: linha expansível ou "mais quente + média".
- [ ] Ventoinhas N slots: CPU fan, chassis 1..N, GPU fan (hoje 1 `FanSpeed` + `GpuFan`).
- [ ] Disco por drive: read/write/usage **com nome do disco** (hoje agregado),
      NVMe: temperatura, % de saúde (SMART), total gravado (TBW).
- [ ] VRM e rails: temps VRM (VROUT/VRIN), +12V/+5V/+3.3V, chipset, EC de laptop.
- [ ] Energia: réguas de CPU package + por dominio, power limit ativo/motivo.
      (Enum já tem `CpuPower`/`GpuPower`; mapear de fato.)
- [ ] Rede por interface com total acumulado da sessão (hoje agregado).
- [ ] `HardwareInventory`: relatório de hardware detectado (nomes/versões/SMART/SPD) —
      só uma tela de inventário, mesma fonte LHM.
- [ ] Overlay/painel: linhas configuráveis por série (checkbox inline no overlay
      editor por "Gpu0 Core", "NVMe #0 Temp"…) em vez de seção fixa.

## P2 — SDK de telemetria via memória compartilhada ("efeito RTSS/HWiNFO SDK")

> Terceiros (AHK, dashboards, plugins OBS) consumindo você, não só o inverso.

- [ ] `MemoryMappedFile` versionada (`phantom_monitor_telemetry_v<NN>`) com
      snapshot estruturado gravado in-place a cada ciclo (alvo 100 ms cadência).
- [ ] Layout com header (magic/version/timestamp/counter) e tabela de séries
      nomeadas (id → type/unidade/label); tester/leitor de exemplo em `docs/`.
- [ ] Consistência de leitura: seqlock (contador de versão) — referência: o header
      do `PhantomVulkanTelemetry.h` (nota ali explica por que não ficou seqlock).
- [ ] Manter `sensors_latest.json` como fallback legado (deprecando devagar).
- [ ] Documentar no README link para um `docs/telemetry-sdk.md` com layout e exemplos.

## P3 — Per-app profiles do OSD

> Você já detecta a janela em foco a cada 30 ms; faltam perfis por processo.

- [ ] Config de perfil por projeto/executável (layout, posições, séries visíveis,
      tamanho/opacity/accent) — herda do perfil global por padrão.
- [ ] UI: aba de perfis no Overlay Editor (combo "Aplicado a este jogo?").
- [ ] Persistência em `config.json` (`profiles: [{ app, overrides }]`).
- [ ] Hotkey/tray: trocar perfil manualmente + indicador ativo no overlay.

## P4 — OSD in-frame no Windows (o único gap "grande")

> É o que faz o RTSS aparecer DENTRO de gravações/streams (OBS game capture,
> exclusive fullscreen). Conflita com a filosofia "sem injeção" do projeto →
> **opt-in explícito, com o trade-off de anti-cheat documentado no README**.

- [ ] Estudo de arquitetura: DLL injetada que intercepta `Present`
      (`IDXGISwapChain::Present` / D3D12 / Vulkan) e desenha o OSD no próprio frame
      — o hook Vulkan de Linux ("fix(vulkan)") já é o protótipo do design.
- [ ] Comunicação da DLL com o monitor via o mesmo padrão SHM (P2).
- [ ] Render de texto: DirectWrite/D2D sobre o backside do swapchain.
- [ ] Flag de risco na UI (checkbox "modo hook, risco em jogos com anti-cheat") +
      lista de aplicativos permitidos (whitelist ao inverso do gating atual).
- [ ] **Manter o overlay WPF como default** (risco zero) e tudo com opt-in.

## P5 — Controle (OC/ventoinha/limiter) — head-on suspicious "produto diferente"

> Depende de escrita em hardware (driver de kernel/via NVAPI/ADLX). Não
> recomendado dentro do mesmo processo do monitor — se um dia entrar, é um
> app/pacote separado.

- [ ] (decisão explícita de NÃO fazer no curto prazo; mantém o escopo "read-only")

## Deliberadamente NÃO planejado (decisões já tomadas)

- Seqlock na SHM Vulkan (custo de 4 bytes/ABI por consistência total) —
  leitura eventual documentada; o leitor filtra valores plausíveis.
- Placeholders ("--°C/-fps") até o primeiro dado: cosmético, mantido.
- 1%/0.1% Low com <100 amostras ≈ mínimo: é a definição matemática; não mudar.
