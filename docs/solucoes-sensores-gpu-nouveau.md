# Alternativas para sensores de GPU no Linux com Nouveau

## Situação observada

Na máquina investigada, a NVIDIA GTX 1660 SUPER está usando o driver Nouveau. O
driver identifica a placa, mas não publica interfaces legíveis para as métricas
de uso, VRAM e temperatura consultadas pelo Phantom Monitor. Sem uma leitura
fornecida pelo driver/kernel, o aplicativo não consegue exibir um valor real;
estimar esses dados pelo modelo da placa produziria telemetria enganosa.

Os avisos atuais na tela de Diagnóstico descrevem, por placa e métrica, as fontes
consultadas e o motivo da indisponibilidade. Eles são avisos de estado, não
exceções do aplicativo, portanto não entram no `errors.log`.

## Opções

### 1. Manter Nouveau e aguardar suporte de telemetria no kernel

Uma proposta para o Nouveau usa dados RUSD publicados pelo firmware GSP para
expor temperaturas, utilização e métricas agregadas de VRAM por `hwmon`, `sysfs`
e `fdinfo`. Isso pode permitir que ferramentas de monitoramento leiam dados sem
usar o driver NVIDIA proprietário. A série consultada ainda estava em revisão e
não havia sido incorporada ao kernel; portanto, uma atualização comum do sistema
não garante que os canais já estejam disponíveis.

Quando esse suporte chegar a um kernel estável da distribuição, o Phantom
Monitor também precisará reconhecer as interfaces que o driver publicar. Os
diagnósticos atuais permitem confirmar se as fontes passaram a existir.

Fonte: [proposta de telemetria GSP/RUSD para Nouveau e discussão de revisão](https://patchew.org/linux/20260714211429.238355-1-mohamedahmedegypt2001%40gmail.com/).

### 2. Usar o driver NVIDIA compatível

É a opção prática para obter métricas imediatamente nessa configuração. O driver
NVIDIA fornece NVML, que pode publicar utilização, memória e temperatura. A
disponibilidade de hotspot e temperatura da VRAM ainda depende do sensor e do
driver; não há garantia de que todas as seis métricas do painel apareçam.

O Phantom Monitor já tenta carregar NVML quando ela está instalada e mantém o
diagnóstico por fonte. Não é necessário instalar `nvidia-smi` para o aplicativo
ler NVML.

### 3. Testar um kernel Nouveau com a proposta aplicada

É possível testar antes da distribuição empacotar o suporte, usando um kernel
experimental com as alterações de telemetria. Isso exige compilar ou instalar
um kernel modificado, confirmar que ele suporta a placa e manter um kernel
anterior para inicialização alternativa. A proposta ainda estava em revisão na
fonte consultada, então não é recomendada como configuração diária.

### 4. Usar uma combinação GPU/driver que publique as métricas desejadas

Para uma compra ou troca futura, conferir previamente quais métricas o driver
Linux daquela GPU realmente publica em `hwmon`, DRM ou NVML. A cobertura varia
entre fabricantes, modelos, gerações e drivers; não se deve assumir que todas as
placas expõem temperatura de hotspot ou de VRAM.

## O que não resolve a ausência da interface

`lm-sensors` e ferramentas semelhantes podem apresentar canais que já existam em
`hwmon`, mas não criam leituras que o driver não publicou. O aplicativo também
não deve somar estatísticas por processo para inventar uso agregado de memória,
nem apresentar RAM compartilhada como VRAM.

## Resumo para esta máquina

- **Sem trocar de driver:** aguardar o suporte Nouveau chegar ao kernel da
  distribuição e depois confirmar os canais nos Diagnósticos.
- **Para leituras agora:** usar o driver NVIDIA compatível e verificar quais
  métricas a placa disponibiliza.
- **Para experimentar antes:** usar um kernel de teste com a proposta, sabendo
  que é uma solução experimental.

