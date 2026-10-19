# Atalho Global (HotkeyService / HotkeyParser)

Permite mostrar/ocultar o overlay OSD a partir de qualquer aplicativo — inclusive com um jogo em foco — sem injeção de DLL nem hooks de teclado: usa o `RegisterHotKey` do Win32, que notifica a janela principal via `WM_HOTKEY`.

## HotkeyParser

Converte a especificação textual do `config.json` (`toggleOverlayHotkey`) em modificadores e virtual-key do Win32:

- Modificadores aceitos: `Ctrl`/`Control`, `Alt`, `Shift`, `Win`/`Windows` (separados por `+` ou espaço).
- Teclas aceitas: `A–Z`, `0–9` e `F1–F24`.
- Regra de segurança: a combinação precisa ter pelo menos um modificador **ou** ser uma tecla de função — evita capturar teclas soltas como `A` ou `5` globalmente.
- Exemplos válidos: `Ctrl+Shift+O`, `Ctrl+Alt+F10`, `F9`, `Win+Shift+P`.

## HotkeyService

- Usa `RegisterHotKey` com `MOD_NOREPEAT` (sem repetição ao segurar a tecla) sobre o HWND da janela principal (WPF `HwndSource`).
- Mensagens `WM_HOTKEY` chegam ao WndProc da janela e disparam o callback que alterna o toggle "Overlay" da janela principal (mesmo caminho do clique manual).
- `Register()` retorna `false` (e grava em `logs/errors.log`) quando a especificação é inválida ou a combinação já está em uso por outro aplicativo.
- Registro/remoção acontecem na carga do app, ao aplicar o Editor de Overlay e ao fechar a janela principal.
- **Múltiplos atalhos (P6.7):** o service aceita um `hotkeyId` Win32 por instância — o app roda **duas** instâncias no mesmo HWND: overlay (`0x5048`, default) e pausa do monitoramento (`0x5049`), cada um com `WM_HOTKEY` próprio, sem colisão.

## Configuração

- `toggleOverlayHotkey` (string, padrão `""` = desativado): atalho global para mostrar/ocultar o OSD.
- `pauseMonitorHotkey` (string, padrão `"Ctrl+Alt+P"` = ativo): atalho global que **pausa/retoma o monitoramento** (P6.7) — ciclos param, CSV/JSON/SDK congelam no último valor e o overlay exibe o último snapshot; estado aceita não sobreviva ao reinício do app. Vazio = desativado.
- Editáveis no `config.json` (o Editor de Overlay cobre o do OSD); não requerem privilégios administrativos e funcionam independente do aplicativo em foco.
- O atalho de overlay apenas alterna a visibilidade do overlay — o OSD continua sendo desenhado pela janela topmost do próprio Phantom Monitor.
