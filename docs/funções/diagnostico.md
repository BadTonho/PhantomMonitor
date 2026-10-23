# Diagnóstico do aplicativo

> **Idioma:** Português (Brasil) · [English](../en/functions/diagnostics.md)

O centro de diagnóstico reúne erros registrados e o estado das fontes de sensores. Ele ajuda a distinguir falha do aplicativo de uma leitura que o hardware, o kernel ou o driver não publicou.

## Acesso

- **App único (Windows e Linux):** abra **Diagnóstico** na navegação lateral. A tela inicia em **Resumo**, com o estado do monitoramento, a contagem de erros recentes e a ocorrência mais nova. Use **Erros** e **Sensores** para abrir as listas detalhadas. Novos registros aparecem em tempo real; **Atualizar** relê o arquivo local e atualiza o estado a partir da última amostra. **Abrir arquivo de erros** abre o log no aplicativo padrão do sistema.

A opção **Erros** separa **Avisos atuais de sensores GPU** dos **Erros registrados**. Os avisos mostram uma linha por métrica indisponível, com a placa, fonte e motivo; atualizam junto com as amostras, desaparecem quando a leitura volta e não são gravados no `errors.log`. Se nenhuma GPU for enumerada, aparece um aviso geral. Os erros registrados são até 500 ocorrências recentes, em ordem cronológica, com horário, contexto e mensagem. Exceções incluem seus detalhes técnicos. O log permanece local; o aplicativo não envia relatórios.

## Local e retenção

- **Linux (app único):** `$XDG_DATA_HOME/PhantomMonitor/logs/errors.log`; normalmente `~/.local/share/PhantomMonitor/logs/errors.log`.
- **Windows (app único):** `%LOCALAPPDATA%\PhantomMonitor\logs\errors.log` (o frontend WPF antigo gravava `logs/errors.log` ao lado do executável).

O arquivo é limitado a aproximadamente 1 MB e mensagens repetidas consecutivamente são suprimidas. Se a gravação do arquivo falhar, o registro ainda pode aparecer na lista em memória durante a execução atual.

## Sensores e fontes

Cada leitura conhecida é descrita como disponível ou indisponível, com a fonte consultada e uma explicação quando possível. A ausência de um sensor opcional é informativa, não uma exceção.

No Linux, os detalhes incluem as fontes `procfs`, `sysfs`, `hwmon`, DRM, NVML e a Phantom Vulkan Layer. Para cada placa detectada, a tela mostra vendor, driver, PCI e a fonte usada por métrica, além do motivo quando não há leitura. Para NVIDIA, informa se `libnvidia-ml.so.1` foi carregada e separa as métricas de NVML, DRM e `hwmon`. Em Intel `i915`/`xe`, mostra o estado dos contadores `drm-engine-*` em `fdinfo`; a primeira leitura estabelece uma base e a utilização é calculada no intervalo seguinte. Acesso restrito a processos pode impedir essa leitura sem afetar os demais sensores. Nouveau pode ser identificado mesmo quando o driver não publica uso, VRAM ou canais térmicos; nesses casos os valores permanecem indisponíveis e o diagnóstico explica as fontes consultadas.

No Windows, a aba de sensores lista hardware e leituras encontrados pelo LibreHardwareMonitor, o estado do PresentMon/ETW e a disponibilidade dos tipos de série conhecidos. A aba **Erros** também mostra avisos atuais para as seis métricas GPU do painel quando o LibreHardwareMonitor não publica uma leitura válida.

## Limites

O aplicativo registra exceções que consegue observar e descreve fontes conhecidas pelo leitor. Ele não consegue explicar falhas que não chegam ao processo nem inferir um valor que o dispositivo ou driver não expõe. Para compartilhar um diagnóstico, abra o arquivo e revise-o antes de enviá-lo: caminhos e mensagens locais podem conter informações da máquina.
