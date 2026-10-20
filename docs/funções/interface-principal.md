# MainWindow

Descrição da interface principal WPF do Phantom Monitor para Windows, com tema escuro e layout de sidebar + conteúdo. O novo dashboard Avalonia para Linux está descrito em [suporte-linux.md](suporte-linux.md); nesta etapa ele cobre apenas a visão geral de sensores e FPS.

## Funcionalidades

- **Sidebar Compacta:** Status da execução (`Monitorando` / `Parado` / `Pausado` (P6.7)), tempo ativo, contagem de sensores, navegação rápida, toggle `Overlay no Jogo`, atalhos para o Editor de Overlay e o Inventário de Hardware, controles de exportação (CSV/JSON/Journal JSONL — P8.3) e opção `Iniciar com o Windows` (registro `Run`, aplicada na hora).
- **Bandeja do Sistema:** Ícone com menu de contexto (Mostrar janela, Overlay no Jogo, Iniciar com o Windows, Resetar estatísticas, Pausar monitoramento, seção de perfis do OSD e Sair) — ver [tray.md](tray.md).
- **Cards de Métricas Clean:** Cards dedicados para Processador (CPU), Placa de Vídeo (GPU), Taxa de Quadros (FPS), Memória RAM, Armazenamento (Disco), Rede (↓/↑) e Placa-mãe & Ventoinhas (maior temperatura + ventoinhas GPU/Sistema).
- **Linhas de Detalhes:** Os cards de CPU (clock, potência, voltagem) e GPU (VRAM usada/total, clock, potência, hotspot e temp de VRAM — P8.6) exibem uma linha secundária com os valores avançados disponíveis; quando nenhum sensor existe, informam a indisponibilidade.
- **Indicadores Visuais Sutis:** Barras de progresso horizontais finas (5px) e badges em pílula com borda translúcida para temperatura e frametime (sem efeitos neon ou círculos exagerados).
- **Lista de Processos Ativos:** Visualização limpa em tabela de processos consumindo a GPU.
- **Janela de Diagnóstico:** Acesso direto à visualização de sensores detectados em tempo real (`DebugWindow`).
- **Aviso de Elevação:** Alerta discreto quando a aplicação não possui privilégios administrativos.

## Auto-inicialização

O monitoramento inicia automaticamente ao abrir o aplicativo (com fallback seguro e suporte a controle manual via "Iniciar Monitor" e "Parar"). A pausa (P6.7) é só de runtime — via bandeja ou atalho global — e reiniciar o app retoma a coleta.

## Cores e Tipografia

- **Paleta Base:** Neutros escuros (`#0F1117` para fundo, `#141720` para sidebar, `#181B24` para cards com borda 1px `#232734`).
- **Tipografia Principal:** Textos em `#F1F5F9` (alto contraste sem saturação agressiva), rótulos em `#94A3B8` e legendas em `#64748B`.
- **Acentos Funcionais:** Azul royal (`#2563EB` / `#3B82F6`) para ações principais e barras de progresso.
- **Faixas de Temperatura e Uso Dinâmico:**
  - Uso < 75%: Branco/Slate neutro (`#F1F5F9`) para uso normal.
  - Uso 75% a 90%: Âmbar suave (`#F59E0B`).
  - Uso ≥ 90%: Coral (`#EF4444`).
  - Temperatura: Pílulas discretas com tons esmeralda (≤ 65°C), âmbar (≤ 80°C) ou coral (> 80°C).

## Elevação de privilégio

Se o aplicativo não estiver rodando como Administrador, exibe um banner com botão para reiniciar com privilégios elevados (necessário para a leitura irrestrita da CPU e rastreamento via PresentMon).
