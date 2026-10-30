# Bandeja do Sistema (TrayIconService) e Inicialização com o Windows (StartupManager)

> **Idioma:** Português (Brasil) · [English](../en/functions/system-tray.md)

Integração com a bandeja **sem WinForms**: usa `Shell_NotifyIcon` do Win32 e o ícone é desenhado em runtime, sem assets externos nem pacotes adicionais (o WPF aposentado desenhava com `RenderTargetBitmap` → `CreateDIBSection` → `CreateIconIndirect`; o app único usa a API `TrayIcon` do Avalonia — seção abaixo).

## TrayIconService (implementação do WPF aposentado)

- **Ícone:** quadrado escuro (`#0B1220`) com borda azul (`#1E90FF`) e uma linha de gráfico ciano (`#00E5FF`), coerente com a identidade do OSD. Em caso de falha no desenho, cai para o ícone padrão do sistema (`IDI_APPLICATION`).
- **Notificações:** versão 4 do protocolo (`NIM_SETVERSION`), que entrega eventos modernos (`NIN_SELECT`, `WM_CONTEXTMENU`) e respeita DPI.
- **Clique esquerdo/duplo:** traz a janela principal para frente (restaura se minimizada).
- **Clique direito — menu de contexto:**
  - `Mostrar Phantom Monitor` — ativa a janela principal.
  - `Overlay no Jogo` (check) — mesma ação do toggle de overlay da sidebar.
  - `Iniciar com o Windows` (check) — liga/desliga a inicialização automática.
  - `Resetar estatísticas` — zera min/máx/média das séries da sessão (ver
    [hardware-monitor](hardware-monitor.md)) e grava um marcador `#marker` no CSV
    ativo (e uma nota equivalente no journal JSONL ativo, quando ele estiver ligado).
  - `Pausar monitoramento` (check) — pausa/retoma a coleta (P6.7): ciclos pulados,
    CSV/JSON/SDK congelados no último valor, overlay fixo no último snapshot;
    estado é só runtime (não persiste). Alternativa: atalho global
    `pauseMonitorHotkey` (padrão `Ctrl+Alt+P`, ver [hotkey](hotkey.md)).
  - `Sair` — encerra o aplicativo.
  - Após `Pausar monitoramento` (e antes de `Sair`, em um separador próprio), vem a
    **seção dinâmica de perfis** do OSD (fixar perfil / voltar ao automático),
    avaliada a cada abertura do menu.
- **Robustez:** falhas de criação são registradas em `logs/errors.log` (eventos `TrayIcon`) sem derrubar o app; o ícone é removido (`NIM_DELETE`) e o hook de janela é desfeito no `Dispose` (fechamento da janela principal).

## App unificado (Avalonia, F1.5)

O app único traz a mesma bandeja via API `TrayIcon` do Avalonia
(`src/PhantomMonitor.App/Services/TrayIconService.cs`), com o mesmo menu e o mesmo
ícone desenhado em runtime (quadrado escuro + gráfico ciano; sem assets externos):

- **Windows:** o mesmo protocolo `Shell_NotifyIcon` (por baixo da API do Avalonia).
- **Linux:** integração por **StatusNotifier/DBus** — funciona em desktops com
  suporte (KDE, GNOME com a extensão/painel de área de notificação); em
  *desktops* sem StatusNotifier o ícone simplesmente não aparece e o app segue
  funcional (o estado de overlay/pausa continua pela sidebar/atalhos quando houver).
- **Menu:** mesmos itens herdados do WPF (`Mostrar Phantom Monitor`, `Overlay no Jogo`,
  `Iniciar com o sistema`, `Resetar estatísticas`, `Pausar monitoramento`, seção
  dinâmica de perfis e `Sair`), com checks reconstruídos quando o estado muda
  (`InvalidateMenu`), clique esquerdo no ícone ativando a janela e o item de perfil
  fixando o pin do P3 (`SetPinnedProfile` — sessão, não persiste no config).
- `Resetar estatísticas` do app unificado zera min/máx/média e grava o marcador
  `#marker reset (inventário)` no CSV ativo (mais a nota equivalente no journal
  JSONL ativo) — mesma semântica da janela de inventário (ver
  [inventário](inventário-hardware.md)).

## StartupManager (Windows / XDG autostart no Linux)

O `StartupManagerFactory` (Core) devolve a implementação do SO:

- **Windows (`WindowsStartupManager`):** escreve/remove a entrada
  `PhantomMonitor` em `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` com o
  caminho do executável atual entre aspas (sem exigir administrador) — mesmo
  comportamento herdado do WPF aposentado.
- **Linux (`LinuxStartupManager`):** entrada XDG autostart
  `~/.config/autostart/phantom-monitor.desktop` com `Exec="<executável atual>"`;
  `IsEnabled()` valida que a entrada ainda aponta para este executável.
- Ambos: `SetEnabled(bool)` retorna `false` (e registra erro em
  `logs/errors.log`) em caso de política restrita/falha; a UI relê o estado real
  após a tentativa (checkbox volta ao estado vivo, comportamento herdado do WPF).

## Interface

- **Configurações → INICIALIZAÇÃO:** checkbox `Iniciar com o sistema` (aplicado na hora, estado lido do registro/autostart na abertura).
- **Menu da bandeja:** itens `Overlay no Jogo` e `Iniciar com o sistema` com marcação de check do estado atual (no app único).

## Observações

- O ícone entra na área de overflow da bandeja por padrão (comportamento do Windows para ícones novos); o usuário pode fixá-lo na barra visível.
- Em builds de desenvolvimento, o caminho registrado aponta para o executável de `bin/…` correspondente — o comportamento é o mesmo de uma instalação publicada.
