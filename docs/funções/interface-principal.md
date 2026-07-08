# MainWindow

Interface gráfica principal do Riva Monitor. Utiliza WPF com tema escuro e layout de sidebar + conteúdo.

## Funcionalidades

- Sidebar com status, navegação por sensores e controles
- Cards de sensores na área principal (CPU, GPU, RAM, Disco)
- Círculos de progresso com porcentagem e temperatura
- Painel de processos da GPU
- Expansor de debug com todos os sensores detectados
- Botão de fechar (sobreposição no canto superior direito)
- Aviso de administrador com botão para reiniciar como Admin

## Auto-inicialização

O monitoramento inicia automaticamente ao abrir o aplicativo (não requer clique manual em "INICIAR").

## Cores dinâmicas

- Uso < 50%: verde (#00FF88)
- Uso < 80%: laranja (#FFBB00)
- Uso ≥ 80%: vermelho (#FF4444)

## Temperatura

- ≤ 60°C: verde
- ≤ 80°C: laranja
- > 80°C: vermelho
- Gradiente radial no fundo do círculo

## Elevação de privilégio

Se o app não estiver rodando como Administrador, exibe um aviso vermelho com botão para reiniciar com privilégios elevados (necessário para leitura completa de sensores de hardware).
