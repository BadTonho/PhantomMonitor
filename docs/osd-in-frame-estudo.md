# OSD In-Frame no Windows — Estudo de Arquitetura (P4)

**Status:** estudo de arquitetura. Nenhuma DLL nativa nova, nenhum hook, nenhum
schema de config foi adicionado — a decisão deixa isso explícito por segurança
(anti-cheat) e pela filosofia do projeto descrita no README.

## 1. O problema

O `OverlayWindow` atual é uma janela WPF `WS_EX_LAYERED` **separada** do jogo:

- aparece no desktop e em `window capture`/`display capture` do OBS;
- **não aparece** em `game capture` (capture de swapchain): o compositor captura
  apenas os quadros que o jogo apresenta — janelas de outros processos ficam de fora.

Esse é o gap estrutural em relação ao RTSS: overlay “in-frame”, dentro das
imagens que o jogo apresenta, visível em qualquer captura e em gravações do
Driver-level/GPU capture.

## 2. Alternativas avaliadas

| Opção | Nível | Risco | Decisão |
|---|---|---|---|
| Hook de `Present`/DXGI via DLL injetada no processo do jogo | user-mode hook | **Alto**: a assinatura típica de cheat; bans com EAC/Vanguard/BattlEye são comuns | ❌ contra a filosofia do projeto (sem injeção) |
| Camada Vulkan própria injetável no Windows | driver/layer | Mesmo risco do acima + manutenção por API gráfica | ❌ fora do Windows |
| DirectComposition / Hardware overlay plane (DWM) | sistema | Overlay permanece fora do swapchain do jogo (só improve composição própria) | ❌ não resolve game capture |
| **Plugin OBS que consuma o SDK de telemetria (P2) e renderize dentro do pipeline do OBS** | fora do jogo | **Zero**: toca apenas na memória compartilhada; o stream embute o OSD | ✅ caminho recomendado |

## 3. Design recomendado: plugin OBS + SDK de telemetria

A infraestrutura do P2 (`docs/telemetry-sdk.md`) já fornece, sem parse:

- memória compartilhada `phantom_monitor_telemetry_v1` com seqlock;
- chaves estáveis `Categoria` / `Categoria#índice` (`SensorKey`);
- unidades (`SensorUnits`) e rótulos de origem.

O plugin (C/C++ via `obs-frontend-api`/`obs-graphics`) faria:

1. **Fonte de dados:** thread dedicada lendo o mapa (mesma sequência do
   `TelemetrySdkReader` de exemplo: magic → version → seqlock duplo) e
   mantendo um cache snapshot a snapshot.
2. **Render:** `obs_source` custom desenhando as linhas no `render()` do OBS
   (já roda dentro da cena capturada, portanto in-frame para game capture e
   gravações). Layout segue o mesmo desenho do OSD Phantom (tags de família
   `GPU1`, `FAN2`... + colunas tabulares monoespaçadas).
3. **Filtro/séries:** reutiliza a whitelist `overlay.shownSeries`
   (wildcards de `SensorKey`) e a ordem por família já implementada no
   `OverlayWindow` — paridade de configuração entre o OSD desktop e o in-frame.
4. **Distribuição:** plugin separado (não assinado) vai ao próprio repositório do
   plugin (fora desta solução); o monitor continua com a arquitetura atual.

### Esforço estimado (fases)
1. Fonte de leitura C++ com seqlock (≈1 day; refere-te `TelemetrySdkReader` em C#).
2. `obs_source` de texto em canvas dos itens formatados (2–4 days).
3. Gráfico de frametime antes (cross-OS: dados já publicados em `FrametimeHistory`?)
   — v2; somente texto primeiro.

Total ≈ 1 semana para o MVP texto-only.

## 4. Por que NÃO hooks/injeção (política)

- Anti-cheats modernos (EAC, BattlEye, Vanguard...) detetam tipicamente DLLs
  estranhas no processo do jogo; hooks de `Present` são a assinatura clássica
  de cheats de ESP. Sem injeção, os mesmos consumidores podem usar o monitor
  em **qualquer** jogo, mesmo em rankeds.
- O projeto já tem in-frame-OSD no **Linux** por uma via que não fala com o
  processo do jogo: a camada Vulkan própria (`PhantomVulkanLayer`) é uma
  VK_LAYER opt-in ativada por variável de ambiente do jogo, lendo o mesmo
  formato de memória compartilhada POSIX `/dev/shm/phantom_fps_*` — sem DLL
  injetada em processos alheios. O estudo de portar esse modelo ao Windows
  continua descartado: no Windows não existe o hook de camada equivalente sem
  injeção de DLL no processo alvo.

## 5. O que muda no produto hoje

Nada em código. O roteiro de implementação fica registrado neste documento;
se um dia o plugin OBS for implementado, o SDK de P2 já é o contrato.
