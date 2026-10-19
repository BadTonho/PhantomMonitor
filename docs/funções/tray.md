# Bandeja do Sistema (TrayIconService) e Inicialização com o Windows (StartupManager)

Integração com a bandeja do Windows **sem WinForms**: usa `Shell_NotifyIcon` do Win32 puro sobre o HWND da janela principal, e o ícone é desenhado em runtime com WPF (`RenderTargetBitmap` → `CreateDIBSection` → `CreateIconIndirect`) — sem assets externos nem pacotes adicionais.

## TrayIconService

- **Ícone:** quadrado escuro (`#0B1220`) com borda azul (`#1E90FF`) e uma linha de gráfico ciano (`#00E5FF`), coerente com a identidade do OSD. Em caso de falha no desenho, cai para o ícone padrão do sistema (`IDI_APPLICATION`).
- **Notificações:** versão 4 do protocolo (`NIM_SETVERSION`), que entrega eventos modernos (`NIN_SELECT`, `WM_CONTEXTMENU`) e respeita DPI.
- **Clique esquerdo/duplo:** traz a janela principal para frente (restaura se minimizada).
- **Clique direito — menu de contexto:**
  - `Mostrar Phantom Monitor` — ativa a janela principal.
  - `Overlay no Jogo` (check) — mesma ação do toggle de overlay da sidebar.
  - `Iniciar com o Windows` (check) — liga/desliga a inicialização automática.
  - `Resetar estatísticas` — zera min/máx/média das séries da sessão (ver
    [hardware-monitor](hardware-monitor.md)) e grava um marcador `#marker` no CSV ativo.
  - `Pausar monitoramento` (check) — pausa/retoma a coleta (P6.7): ciclos pulados,
    CSV/JSON/SDK congelados no último valor, overlay fixo no último snapshot;
    estado é só runtime (não persiste). Alternativa: atalho global
    `pauseMonitorHotkey` (padrão `Ctrl+Alt+P`, ver [hotkey](hotkey.md)).
  - `Sair` — encerra o aplicativo.
  - Após `Iniciar com o Windows`, vem a **seção dinâmica de perfis** do OSD
    (fixar perfil / voltar ao automático).
- **Robustez:** falhas de criação são registradas em `logs/errors.log` (eventos `TrayIcon`) sem derrubar o app; o ícone é removido (`NIM_DELETE`) e o hook de janela é desfeito no `Dispose` (fechamento da janela principal).

## StartupManager

- Escreve/remove a entrada `PhantomMonitor` em `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` com o caminho do executável atual entre aspas (sem exigir administrador).
- `IsEnabled()` compara o valor do registro com o caminho do executável **atual** (aceita com/sem aspas, ignorando caixa) — se o app for movido, um registro antigo não é considerado ativo e o checkbox reflete isso.
- `SetEnabled(bool)` retorna `false` (e registra erro) em caso de política restrita; a UI relê o estado real após a tentativa.

## Interface

- **Sidebar:** checkbox `Iniciar com o Windows` (aplicado na hora, estado lido do registro na abertura).
- **Menu da bandeja:** itens `Overlay no Jogo` e `Iniciar com o Windows` com marcação de check do estado atual.

## Observações

- O ícone entra na área de overflow da bandeja por padrão (comportamento do Windows para ícones novos); o usuário pode fixá-lo na barra visível.
- Em builds de desenvolvimento, o caminho registrado aponta para o executável de `bin/…` correspondente — o comportamento é o mesmo de uma instalação publicada.
