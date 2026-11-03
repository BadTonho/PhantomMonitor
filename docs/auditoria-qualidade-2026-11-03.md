# Auditoria de qualidade — 2026-11-03

> **Idioma:** Português (Brasil) · Documento de análise; **não release/diretriz** — registro pontual de achados para backlog.
> **Escopo:** snapshot do repositório em `96de37b` (`feat: add ShowName option for overlay metrics…`), Windows (build local).
> **Status do repositório ao auditar:** working tree limpo; arquivo novo acrescentado nesta auditoria **sem commit**.

## Metodologia e baseline

- `dotnet build` da app única: **0 avisos / 0 erros**.
- `dotnet test` (`PhantomMonitor.OverlayRegressionTests`): **122 aprovados, 1 ignorado** (skip esperado: teste POSIX em Windows).
- Auditoria em três frentes (Core ≈10k linhas C#, App ≈15k linhas C#/AXAML, `PhantomNativeOverlay` ≈55k linhas C++ — das quais ~96% é código vendido: Dear ImGui 1.91.9b com um patch próprio e MinHook).
- Os achados de maior gravidade foram **re-verificados manualmente** contra o código-fonte (marcados ✅). Itens marcados *⚠ relatado* foram reportados pelo agente auditor com boa evidência, porém sem reconfirmação linha a linha nesta revisão.
- **Falsos alarmes descartados** ao final — importantes para não re-cultivar suspeitas já encerradas.
- **Não auditado a fundo:** `src/PhantomVulkanLayer` (1,6k linhas, Linux-only) e o restante do ImGui vendido.

Legenda de gravidade: **HIGH** = potencial de crash/corrupção · **MED** = comportamento errado confirmável · **LOW** = latente, cosmético ou nicho.

---

## 🔴 A. Camada nativa de overlay em jogo (`src/PhantomNativeOverlay`)

Área de maior risco do projeto (hooks em jogos DirectX). Achados ordenados por gravidade:

### A1. [HIGH] Janela de use-after-free no detour de `Release` ✅

- **Onde:** `windows/PhantomOverlayWindows.cpp:764-779`.
- **Problema:** o detour de `IUnknown::Release` chama `g_release(object)` (o Release original, que pode destruir o objeto ao chegar em 0) **antes** de adquirir `g_render_mutex`. Se o refcount zerar enquanto um Present em outro thread está em voo (mutex em posse do render, que usa a mesma swapchain via `GetBuffer`/`GetDesc`), o objeto é destruído com o render em execução → UAF.
- **Evidência:**
  ```cpp
  ULONG STDMETHODCALLTYPE detour_release(IUnknown* object) {
      const auto remaining = g_release(object);            // destruição possível FORA do lock
      if (remaining == 0 && object == g_active_swapchain) {
          std::lock_guard<std::mutex> render_lock(g_render_mutex);  // lock tarde demais
  ```
- **Fix:** quando `object == g_active_swapchain` (checagem inicial), tomar `g_render_mutex` **antes** de `g_release` e destruir/teardown dentro do lock.

### A2. [HIGH] `Reset` de command list D3D12 em voo ✅

- **Onde:** `windows/PhantomOverlayWindows.cpp:565-618` (caminho crítico 578-582).
- **Problema:** existe **uma única** `g_d3d12_commands` reutilizada em todo frame e back buffer. Antes do `Reset`, o `wait_d3d12_frame(context)` aguarda apenas a fence do frame **atual** (submetida ~2–3 presents atrás). A fence do frame **anterior** — submetida logo antes com a MESMA command list — nunca é aguardada. Com triple buffering/backpressure de GPU, `ID3D12GraphicsCommandList::Reset` sobre lista em execução é ilegal → **device removal dentro do jogo** (o debug layer sinalizaria na hora).
- **Evidência:**
  ```cpp
  if (!wait_d3d12_frame(context)) { ... return; }        // só a fence ESTE frame
  if (FAILED(context.allocator->Reset()) || FAILED(g_d3d12_commands->Reset(context.allocator, nullptr))) {
  ```
  e a submissão por frame no mesmo objeto: `ExecuteCommandLists` (613) + `Signal(g_d3d12_fence, fence_value)` (615).
- **Fix:** aguardar a fence do **último** valor submetido (`g_d3d12_next_fence - 1` / último `fence_value` gravado) antes do `Reset`, ou usar command lists por frame.

### A3. [MED] Vazamento de refs COM no device D3D12 em ciclos de init falhos ✅

- **Onde:** `windows/PhantomOverlayWindows.cpp:426-429` (adoção dupla) + `167-177`/`161-164` (reset solta 1 ref por objeto).
- **Problema:** o init adota o ref do `swapchain->GetDevice` **e** faz `AddRef` extra (2 refs armazenadas), mas `reset_renderer_devices()` solta só 1. No estado estável é equilibrado (`else { device->Release(); }` em 515-517), porém nos caminhos de falha **depois** da adoção (ex.: `ImGui_ImplDX12_Init` falhando em 509-513 → `shutdown_imgui()` deixa `g_api = Unknown`) o ramo de init re-executa **a cada present**, acumulando **+1 ref de device por tentativa**, sem teto.
- **Evidência:**
  ```cpp
  g_d3d12_device = device;
  g_d3d12_queue = queue;
  g_d3d12_device->AddRef();     // ref do GetDevice já é posse suficiente
  g_d3d12_queue->AddRef();
  ```
- **Fix:** remover o `AddRef` do device (o ref do `GetDevice` basta) ou soltar a ref de adoção imediatamente após o `AddRef`; no caminho de falha, chamar `reset_renderer_devices()` (ou recuo de tentativa, ex. 1 por segundo) para o retry começar limpo.

### A4. [MED] Queues `AddRef`'D em `g_observed_queues` quase nunca soltos ✅

- **Onde:** `windows/PhantomOverlayWindows.cpp:645-649` (gravação); nenhum local solta essas refs, exceto a entrada da swapchain ativa (`771-776`).
- **Problema:** cada queue observada mantém uma própria ref; o mapa (teto 64 devices × 8 queues) fixa vida a objetos de fila/devices de swapchains secundárias pela vida do processo.
- **Fix:** expurgar entradas por device no teardown (em `detour_release`/limpeza do device) ou usar rastreamento fraco + ref no lookup.

### A5. [MED] `find_queue` consulta por device pointer morto ✅

- **Onde:** `windows/PhantomOverlayWindows.cpp:620-636`.
- **Problema:** o fallback consulta `g_observed_queues` por `ID3D12Device*` cru que o mapa **não possui** (device solto em 627). Após realocação no mesmo endereço, um device novo casa a entrada morta e recebe a fila de um device estranho (a fila então submete command lists no lugar errado).
- **Fix:** conferir `storedQueue->GetDevice() == device` antes de devolver, ou descartar o fallback por device/chavear por swapchain.

### A6. [MED] Caminho D3D10 salva/restaura apenas 1 render target de 8 ✅

- **Onde:** `windows/PhantomOverlayWindows.cpp:309-314`.
- **Problema:** `OMGetRenderTargets(1, …)` + `OMSetRenderTargets(1, …, nullptr)` destrói os slots 1–7 do jogo a cada frame (Multi-RT não preservado). O caminho D3D11 faz o correto (8 slots, 548-555).
- **Fix:** salvar/restaurar os `D3D10_SIMULTANEOUS_RENDER_TARGET_COUNT` slots (zerando os não usados na chamada de restauração como o D3D11 faz).

### A7. [MED] `MH_ERROR_ALREADY_CREATED` tratado como sucesso → devolus functor nulo no primeiro present ✅

- **Onde:** `windows/PhantomOverlayWindows.cpp:782-787` (e detours em 752-762, 638-657).
- **Problema:** com double-attach (hook já criado por outra instância/módulo), `MH_CreateHook` devolve `MH_ERROR_ALREADY_CREATED` sem necessariamente preencher `original`; o código segue para `MH_EnableHook` → detour ativo com `g_present`/`g_release`/`g_execute_lists` **nulos** → dereferência nula no primeiro chamado.
- **Evidência:**
  ```cpp
  const auto created = MH_CreateHook(target, detour, original);
  if (created != MH_OK && created != MH_ERROR_ALREADY_CREATED) return false;
  return MH_EnableHook(target) == MH_OK || MH_EnableHook(target) == MH_ERROR_ENABLED;
  ```
- **Fix:** tratar `MH_ERROR_ALREADY_CREATED` como falha e/ou guardar guardas de nulo nos detours (`if (!g_present) return E_FAIL;` estilo).

### A8. [MED] Shutdown do backend OpenGL exige contexto corrente ✅

- **Onde:** `windows/PhantomOverlayWindows.cpp:128-129`.
- **Problema:** `ImGui_ImplOpenGL3_Shutdown()` roda apenas se o thread de teardown for o dono do contexto GL; teardown vindo de `detour_release`/`reset_renderer_devices` (em outro thread) destrói o contexto ImGui mas os handles do backend (atlas de fonte, buffers GL) nunca são soltos no contexto dono; o present seguinte re-cria o backend completo do zero.
- **Fix:** teardown adiado: marcar "pending shutdown" e executar dentro do `render_opengl` (contexto corrente) antes do re-init.

### A9. [MED] Espera de fence de 5 s segurando `g_render_mutex` em thread do jogo ✅

- **Onde:** `windows/PhantomOverlayWindows.cpp:138-145` (`wait_d3d12_queue`), invocado por `release_d3d12_resources` → `reset_renderer_devices`, sob `g_render_mutex` (chamadores: `detour_release` em 767-769, `render_present` em 709, detour de ResizeBuffers).
- **Problema:** em device hung/removido, o thread do jogo que tocasse `Release`/`ResizeBuffers` fica travado até 5 s **por retire** e o mutex bloquei os demais.
- **Fix:** encurtar/pular a espera quando o gatilho é teardown (a fence não importa mais) e nunca repetir a espera por processo.

### A10. [MED⚠ relatado] `--inject` habilita hooks sem suspensão de threads

- **Onde:** `windows/PhantomOverlayLauncher.cpp:216-223, 829-848` + `initialize_hooks`.
- **Problema:** o modo `--inject <pid>` (diagnóstico) liga hooks em vtable num processo já rodando; MinHook não garante atomicidade completa amigável a threads executando as funções durante o patch multi-byte. O caminho `--launch` (CREATE_SUSPENDED → injetar → Resume) usado pelo monitor é o seguro.
- **Fix:** documentar/restringir `--inject` a diagnóstico, ou suspender o processo em torno do lote de enable.

### A11. [MED⚠ relatado] Patch próprio no ImGui Vulkan protege índice de frame apenas com assert

- **Onde:** `third_party/imgui/backends/imgui_impl_vulkan.cpp:515-519` (extensão first-party `ImGui_ImplDX12_SetFrameIndex` homóloga no backend Vulkan).
- **Problema:** quota de índice do buffer de render guardada só por `IM_ASSERT`, que cai em builds NDEBUG; se o backend sobreviver a recriação de swapchain com mais imagens, um `wrb->Count` velho gera OOB e uso de `VkBuffer` suspeito/stale.
- **Fix:** guarda de runtime (`if (index >= wrb->Count) { descartar; }`) + rebuild de buffers quando `ImageCount != wrb->Count`.

### A12. [LOW] `working_directory` com `npos` no launcher ✅

- **Onde:** `windows/PhantomOverlayLauncher.cpp:187-189`.
- **Problema:** `executable.substr(0, find_last_of(L"\\/"))` com nome sem caminho (`game.exe`) devolve a string inteira como diretório (`npos`); `CreateProcessW` falha, quando `nullptr` (diretório herdado) teria funcionado.
- **Fix:**
  ```cpp
  const auto pos = executable.find_last_of(L"\\/");
  const auto working_directory = pos == std::wstring::npos ? L"" : executable.substr(0, pos);
  ```

### A13. [LOW] `io.Fonts->Fonts[0]` sem guarda de atlas vazio ✅

- **Onde:** `OverlayImGuiDraw.cpp:116`.
- **Problema:** se todas as fontes TTF falharem **e** `AddFontDefault` falhar (OOM), `Fonts` está vazio → OOB no vector interno a cada draw.
- **Fix:** `if (io.Fonts->Fonts.empty()) return;` antes do índice.

### A14. [LOW⚠ relatado] Mapeamento de shared memory nunca é reaberto

- **Onde:** `OverlayFrameReader.cpp:63-85` (Windows) e via `shm_open` (Linux).
- **Problema:** `Open()` sai cedo se `mapping_ != nullptr`; se o monitor for reiniciado (seção do `Local\PhantomMonitor.InGameOverlay.v1` criada nova), o overlay injetado continua lendo o mapeamento antigo sem recuperação além do backoff de 1 s que nunca re-testa depois.
- **Fix:** em falhas de validade persistentes (magic ausente após N tentativas), `Close()` e permitir reabrir.

### A15. [LOW⚠ relatado] `eglQueryAPI` probeia o estado do thread, não o display passado

- **Onde:** `linux/PhantomOpenGLOverlay.cpp:131-142`.
- **Problema:** `eglSwapBuffers` é interposto por par `(display, surface)`, mas a classificação GL/GLES usa o contexto/API corrente do thread; múltiplos displays podem classificar GLES↔GL errado e passar versão GLSL inválida ao `ImGui_ImplOpenGL3_Init`.
- **Fix:** inferir do display/contexto passados (`eglQueryContext`/`EGL_RENDERABLE_TYPE`).

**Notas positivas da área:** vtable índices hard-coded conferidos contra os headers do SDK 10.0.26100 — **todos corretos**; caminho X11/XWayland de FPS íntegro (ver falsos alarmes); protocolo de texto parseado com limites (fuzz-hardened), seqlock do status correto, ordem de locks única (`g_render_mutex` → `g_swapchain_mutex`), sem deadlock; sem `strcpy/sprintf/alloca` insanáveis no código próprio (os únicos são no ImGui vendido, com buffer próprio).

---

## 🟡 B. PhantomMonitor.Core (sensores e serviços)

### B1. [MED] Totais de rede da sessão absorvem buracos não observados (pause/stop/wake) ✅

- **Onde:** `src/PhantomMonitor.Core/Services/Sensors/Windows/HardwareMonitorService.cs:216-222` (`CollectSnapshot`), interação com o early-return de `Paused` em `Update()` (linha 140).
- **Problema:** os totais acumulam `velocidade × (agora − última amostra)`; durante `Paused` (e após `Stop()`/máquina dormir), `_lastNetSampleUtc` fica congelado. Na retomada, o primeiro tick soma `velocidade × tempo inteiro da pausa` — atribuindo bytes que nunca foram amostrados (e em outras condições, sub-contando). Não há clamp de `elapsed`. `Start()` também não re-baseia `_lastNetSampleUtc,re` após um Stop.
- **Fix:** intervalos maiores que ~2× `_updateIntervalMs` = "não observado" (pular a soma, só re-basear); re-basear a baseline no `Start()` e opcionalmente na retomada de pausa.

### B2. [MED] "Monitor primário" deriva da propriedade errada (`VideoInputType`) ✅

- **Onde:** `src/PhantomMonitor.Core/Services/Sensors/Windows/WindowsMonitorInventoryReader.cs:46`; racional errado documentado em `Models/Inventory/MonitorInfo.cs:40-42`.
- **Problema:** `VideoInputType` de `WmiMonitorBasicDisplayParams` codifica **Analog/Digital** (0=análogo, 1=digital), não primariedade. Consequência: em qualquer setup moderno todo HDMI/DP (tudo digital), **todos** os monitores saem `Primary = true`; em setup só-VGA, nenhum. Impacto atual: flag errada exibida no inventário/JSON (`MonitorInfo.Primary`) — a maquinaria de séries/alertas usa `SensorKey.IsPrimary` (índice 0), que é outra coisa e está correta.
- **Fix:** derivar primário de `EnumDisplayMonitors` + `GetMonitorInfo` (`MONITORINFOF_PRIMARY`) ou simplesmente deixar `Primary = false` até haver fonte real; manter `VideoInputType` como campo informativo.

### B3. [LOW] `_targetPid`/`_targetProcessName` escritos por duas threads sem lock comum ✅

- **Onde:** `src/PhantomMonitor.Core/Services/Sensors/Windows/FpsMonitor.cs:259-263` (thread do CSV do PresentMon) e `339-379` (timer de janela em foco, `CheckForegroundWindow`).
- **Problema:** campos `volatile` evitam tearing individual, mas os dois escritores podem intercalar (o foreground sobrescreve PID recém-auto-escolhido do stream real e vice-versa), rotulando o jogo errado por instantes no `_perfResult`/`TargetProcess*`.
- **Fix:** setter sincronizado único (PID + nome alterados juntos sob um lock) para ambos os caminhos.

### B4. [LOW⚠ relatado] Mensagens de alerta formatadas com cultura do thread atual

- **Onde:** `src/PhantomMonitor.Core/Services/Alerts/AlertRuleEvaluator.cs:66-69`.
- **Problema:** `{value}`/`{threshold}` usam `ToString("F1")` sem `InvariantCulture`, enquanto CSV/JSON/SDK usam Invariant (ex.: `InventoryBuilder.cs:29,47`) → em pt-BR o popup mostra "80,5" e o script recebe "80.5"; builds Linux com invariant globalization invertem a apresentação. Consistência/cosmético.
- **Fix:** `ToString("F1", CultureInfo.InvariantCulture)` nas duas interpolações (ou cultura de UI explícita, escolhida única para todos os sinks).

### B5. [LOW⚠ relatado] Updater: decompressão automática desmonta o check de `Content-Length`

- **Onde:** `src/PhantomMonitor.Core/Services/Update/GitHubReleaseSource.cs:108` (client compartilhado) vs checagem em 69/86.
- **Problema:** com `AutomaticDecompression = DecompressionMethods.All`, se algum hop servir `Content-Encoding: gzip`, o stream já chega descomprimido enquanto `ContentLength` refere ao payload comprimido → falsos "Download truncado" (falha de update válida) ou check silenciosamente sem sentido. Latente: GitHub hoje entrega assets sem gzip e o SHA-256 independente confere (88).
- **Fix:** baixar assets com handler `DecompressionMethods.None` (decompressão só no caminho JSON da API) ou pular o check de length na presença de `Content-Encoding`.

### B6. [LOW⚠ relatado] Dedup do `ErrorLog` agrega por timestamp de segundos

- **Onde:** `src/PhantomMonitor.Core/Services/Logging/ErrorLog.cs:71-76`.
- **Problema:** a consolidação de `fileEntries.Concat(RecentEntries)` agrupa por (`yyyy-MM-dd HH:mm:ss`, contexto, mensagem) — duas ocorrências distintas no mesmo segundo colapsam em uma (só o último timestamp sobra). Só diagnóstico (o arquivo em si já suprime duplicatas consecutivas por design).
- **Fix:** dedup por `Timestamp` de precisão cheia (ticks) ou desambiguação explícita arquivo-memória.

### B7. [Sem defeito, armadilha de indentação] `foreach` sem chaves ✅

- **Onde:** `src/PhantomMonitor.Core/Services/Sensors/Linux/LinuxDiskNetworkReader.cs:170-176`.
- **Estado atual:** o corpo real do loop é só `_prevPerNic.Remove(missing);` — as 5 linhas seguintes (177-176 indentadas) rodam **uma vez após o loop**, que hoje é o comportamento correto (agregados não devem ser republicados por interface). Seu look "block" leva qualquer mantenedor a introduzir um bug ao "consertar".
- **Fix:** envolver explicitamente em `{ }` (no-op de comportamento, ganho de clareza).

---

## 🟢 C. PhantomMonitor.App (UI e serviços do app)

### C1. [MED] Launcher nativo órfão no timeout ✅

- **Onde:** `src/PhantomMonitor.App/Services/Overlay/WindowsOverlayHookService.cs:127-162` (caminho crítico 141-155).
- **Problema:** no timeout de 40 s (`WaitForExitAsync().WaitAsync(...)` → `TimeoutException`), só o handle do objeto `Process` é descartado (`using var process`) — o processo-filho `PhantomOverlayLauncher.exe` **não é morto** e continua rodando, com ninguém drenando stdout/stderr (escritor verboso trava em pipe cheio). Resultado: launcher órfão (com o hook DLL já injetado no jogo) permanece indefinidamente.
- **Evidência:**
  ```csharp
  using var process = Process.Start(startInfo);
  ...
  await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(40));
  ```
- **Fix:** no `catch (TimeoutException)`, `process.Kill(entireProcessTree: true)` best-effort antes de devolver a falha.

### C2. [LOW] Dreno duplo na coalescência de UI ✅

- **Onde:** `src/PhantomMonitor.App/ViewModels/MainWindowViewModel.Snapshot.cs:43-64` (crítico 56-62).
- **Problema:** o callback postado zera `_uiPostActive = 0` **antes** de ler `_pendingUiSnapshot`; se um snapshot chega nessa janela, o CAS do produtor consegue agendar um segundo callback e ambos processam o mesmo payload mais novo (inventário/overlay reconciliados duas vezes). Nada corrompido — só trabalho de UI dobrado (pior no ritmo de tick com jogo).
- **Evidência:**
  ```csharp
  Dispatcher.UIThread.Post(() => {
      _uiPostActive = 0;                 // flag solta antes do dreno
      var latest = _pendingUiSnapshot;
      if (_disposed || latest is null) return;
      UpdateSnapshot(latest);
  });
  ```
- **Fix:** drenar primeiro: `var latest = Interlocked.Exchange(ref _pendingUiSnapshot, null)` (ou ler + anular) **antes** de zerar a flag, e só rodar `UpdateSnapshot` se o payload trocado for não-nulo.

### C3. [LOW⚠ relatado, backend Linux-only] Portal Wayland: sessão fechada pode ficar "ativa" morta

- **Onde:** `src/PhantomMonitor.App/Services/Hotkeys/WaylandOverlayHotkeyBackend.cs:59-80, 183-192`.
- **Problema:** o watch do fechamento da sessão nova é registrado **antes** de `_session = candidate`; se o portal fechar a sessão na janela entre isso e a atribuição (especialmente enquanto `CloseCurrentAsync()` anula `_session`), o callback guarda nada e o `ApplyAsync` marca a combo como "ativa" numa sessão já morta; como `ApplyAsync` curto-circuita quando o draft iguala `_active` (29), re-salvar a mesma combo **não recupera** (só setar em branco e re-salvar). Callbacks DBus e `Dispose` também mexem em `_session`/`_active`/`_display`/`_connection` sem `_gate`.
- **Fix:** montar o estado da sessão nova com re-checagem por handle antes de yield; re-verificar vivacidade do `sessionProxy` após `CloseCurrentAsync()`; rotear callback/`Dispose` pelo `_gate` e re-checar `_disposed` após cada await.

---

## ✅ Falsos alarmes descartados (não re-abrir)

| Suspeita descartada | Motivo |
|---|---|
| Offsets de evento do X11 em `LinuxX11PresentMonitor.cs` (CompleteNotify em byte 36) | Re-verificado contra `xcb_present_complete_notify_event_t` (payload em 8/10/11/12/16/24) e `read_packet()` do libxcb: os offsets do app estão **corretos**; X11/XWayland de FPS funciona. |
| `SensorFormatter.FormatSeries` exibindo fan de GPU em `%` | Consistente com `SensorUnits.Of` (`GpuFan → "%"`, `FanSpeed → "RPM"`); escolha de design (fan = duty da GPU, fan de sistema = RPM). |
| `UpdateService._availableRelease` escritas sem lock | Todas as escritas ocorrem dentro da seção `lock (_gate)` iniciada em ~149. |
| Semântica mista `onlyWhenMissing`/`takeMaximum` em `LinuxGpuSensorReader` | `PublishMetric` (~324-350) é coerente: por instância = primeira fonte ou nada; canônicas = máximo entre NVML/hwmon/thermal. |

## ✅ O que ficou limpo na varredura

- **Higiene managed:** IDisposable/WMI/processos/timers/fds soltos corretamente; sem `async void` nas bibliotecas; handlers de assinatura (VM↔janela, tray, hotkeys) simétricos no `Dispose`/`Closed`.
- **P/Invoke:** dispatch 32/64-bit correto (`GetWindowLongPtr` etc.), assinaturas com `SetLastError`, event-struct offsets do XCB batem, `shm_open/mmap/fchmod` corretas, todos os handles nativos fechados em `finally`.
- **Cultura:** todos os pares parse/format das fronteiras App (config/ports/shared memory) usam cultura casada (Invariant↔Invariant).
- **Nativo (código próprio):** protocolo de texto com limites, seqlock correto, ordem de locks única, sem álgebra de `size_t` não conferida no parser de frames.

## Priorização sugerida (para backlog)

1. **A1 + A2** (nativo, potencial de crash em jogos reais — correções pequenas e localizadas).
2. **A3 + A7** (mesmo arquivo, também pequenos: AddRef extra + guard nulo nos detours).
3. **C1 + C2** (App: kill no timeout + dreno duplo — umas 10 linhas no total).
4. **B1 + B2** (Core: todos os intervalos de rede + fonte correta de "monitor primário").
5. **B3-B7, A4-A6, A8-A15** conforme disponibilidade.
