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

## Configuração

- `toggleOverlayHotkey` (string, padrão `""` = desativado): atalho global para mostrar/ocultar o OSD.
- Editável no Editor de Overlay (seção "ATALHO GLOBAL", com presets) ou direto no `config.json`.
- Não requer privilégios administrativos; funciona independente do aplicativo em foco.
- O atalho apenas alterna a visibilidade do overlay — o OSD continua sendo desenhado pela janela topmost do próprio Phantom Monitor.
