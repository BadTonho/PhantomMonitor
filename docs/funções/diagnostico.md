# Diagnóstico do aplicativo

> **Idioma:** Português (Brasil) · [English](../en/functions/diagnostics.md)

O centro de diagnóstico reúne erros registrados e o estado das fontes de sensores. Ele ajuda a distinguir falha do aplicativo de uma leitura que o hardware, o kernel ou o driver não publicou.

## Acesso

- **Avalonia/Linux:** abra **Diagnóstico** na navegação lateral. A página atualiza os estados junto com os snapshots e recebe novos registros em tempo real. Use **Atualizar** para reler o arquivo local ou **Abrir arquivo de erros** para abri-lo no aplicativo padrão do sistema.
- **WPF/Windows:** na janela principal, clique em **Diagnóstico: sensores e erros do aplicativo**. A aba **Sensores e fontes** acompanha as leituras; a aba **Erros do aplicativo** mostra os registros recentes e permite atualizar ou abrir o arquivo.

As telas exibem até 500 registros recentes, em ordem cronológica, com horário, contexto e mensagem. Exceções incluem seus detalhes técnicos. O log permanece local; o aplicativo não envia relatórios.

## Local e retenção

- **Linux/Avalonia:** `$XDG_DATA_HOME/PhantomMonitor/logs/errors.log`; normalmente `~/.local/share/PhantomMonitor/logs/errors.log`.
- **Windows/WPF:** `logs/errors.log` ao lado do executável.

O arquivo é limitado a aproximadamente 1 MB e mensagens repetidas consecutivamente são suprimidas. Se a gravação do arquivo falhar, o registro ainda pode aparecer na lista em memória durante a execução atual.

## Sensores e fontes

Cada leitura conhecida é descrita como disponível ou indisponível, com a fonte consultada e uma explicação quando possível. A ausência de um sensor opcional é informativa, não uma exceção.

No Linux, os detalhes incluem as fontes `procfs`, `sysfs`, `hwmon`, DRM, NVML e a Phantom Vulkan Layer. Para NVIDIA, a tela identifica a placa e o driver ligado ao dispositivo, informa se `libnvidia-ml.so.1` foi carregada e lista quais métricas vieram de NVML ou `hwmon`. Por exemplo, com Nouveau sem NVML, utilização e VRAM podem não ser fornecidas; temperaturas também dependem de canais publicados pelo driver.

No Windows, a aba de sensores lista hardware e leituras encontrados pelo LibreHardwareMonitor, o estado do PresentMon/ETW e a disponibilidade dos tipos de série conhecidos.

## Limites

O aplicativo registra exceções que consegue observar e descreve fontes conhecidas pelo leitor. Ele não consegue explicar falhas que não chegam ao processo nem inferir um valor que o dispositivo ou driver não expõe. Para compartilhar um diagnóstico, abra o arquivo e revise-o antes de enviá-lo: caminhos e mensagens locais podem conter informações da máquina.
