# Atalhos globais

> **Idioma:** Português (Brasil) · [English](../en/functions/global-hotkey.md)

Em **Configurações → Modo do overlay**, selecione **Definir atalho**, pressione a combinação e clique em **Salvar e aplicar**. A combinação alterna a visibilidade do overlay mesmo com outro aplicativo em foco, tanto no modo flutuante quanto dentro do jogo. **Limpar atalho** desativa a função; `Esc` cancela a captura. O padrão é desativado.

São aceitas letras `A–Z` e números `0–9` com pelo menos um modificador (`Ctrl`, `Alt`, `Shift` ou `Win`), ou teclas `F1–F24` com ou sem modificador. Conflitos com o atalho de pausa ou outra aplicação são informados na interface. A mudança passa a valer após salvar; quando a gravação falha, o app tenta restaurar a combinação anterior e informa se a restauração falhar.

## Plataformas

- **Windows:** `RegisterHotKey` entrega as ações a uma janela message-only em thread dedicada. A troca do overlay preserva o registro de pausa; não há hook de teclado.
- **Linux X11:** usa uma conexão XCB dedicada e registra as variações para Caps Lock, Num Lock e Scroll Lock. Requer `libxcb`; em caso de conflito o registro anterior permanece ativo.
- **Linux Wayland:** usa o portal `org.freedesktop.portal.GlobalShortcuts` pela sessão D-Bus. O ambiente gráfico pode abrir uma confirmação e escolher uma combinação diferente da solicitada; a interface mostra a descrição devolvida pelo sistema. A disponibilidade depende do portal e do compositor. XWayland não é usado como substituto para atalhos globais Wayland.

No Linux, o suporte foi implementado, mas ainda precisa de validação em sessões reais X11 e Wayland. O atalho de pausa continua disponível apenas no Windows. Quando o serviço global não está disponível, a interface e a bandeja continuam permitindo alternar o overlay.

## Configuração

`toggleOverlayHotkey` mantém a combinação preferida no `config.json`, por exemplo `Ctrl+Shift+O`, e continua vazio por padrão. Ela também pode ser escolhida pela interface. `pauseMonitorHotkey` continua configurável no arquivo e usa `Ctrl+Alt+P` por padrão no Windows; vazio desativa a pausa por atalho. Configurações antigas são preservadas.
