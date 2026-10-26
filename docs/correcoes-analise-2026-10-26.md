# Correções da análise técnica — 26/10/2026

Este documento acompanha a implementação dos 21 achados da
[análise de 25/10](analise-tecnica-2026-10-25.md). O relatório anterior descreve
o estado histórico da base; as correções abaixo são o comportamento atual.

## Cobertura dos achados

| Achado | Correção implementada | Verificação |
| --- | --- | --- |
| A01 | Carga atômica somente leitura no canal Windows | Parser real sobre página `PAGE_READONLY`, x86/x64 |
| A02 | Callback/fallback de loader data nos command buffers e backend Vulkan resolvido pela próxima camada | Testes nativos de inicialização do dispatch e compilação sem imports globais |
| A03 | Caminho Windows do manifesto e negociação de interface; exports sem decoração em x86 | Teste de presença dos exports e despacho nas duas arquiteturas |
| A04 | Comandos de atalhos executados na thread da janela | Registro, remoção e novo registro reais no Windows |
| A05 | Atualizador percorre todo o payload, incluindo `PhantomOverlay` | Extração ZIP e transação com DLL em subpasta |
| A06 | Preparação e aplicação AppImage compartilham caminho de versão | Resolução do payload e script real com AppImage fictício |
| A07 | Captura tardia de fila DIRECT inequívoca e lançamento antecipado no Windows | Compilação dos hooks/launcher/UI; validação em jogos continua necessária |
| A08 | GIPA/GDPA preservam os mesmos hooks e disponibilidade da cadeia | Cadeia simulada, incluindo função indisponível |
| A09 | Família de fila indexada pelo próprio `VkQueue` | Revisão do registro/lookup e builds multiplataforma |
| A10 | Cópia preparada, backup persistente e rollback do conjunto completo | Falhas injetadas depois de alterações anteriores, Windows e Linux |
| A11 | Estado independente por objeto de regra | Duas ações com condição idêntica e cooldown |
| A12 | Seleção por frescor, reset de PID/sessão/contador e indisponibilidade de dados expirados | Testes de troca de processo e sessão |
| A13 | Ring v2 de 4.096 frames, com sequência e sessão | Spike entre polls preservado; leitura parcial rejeitada |
| A14 | Ausência limpa cadência, último valor e suavização | Série retorna com valor novo, sem reutilizar 30 em lugar de 90 |
| A15 | Segmentação CSV `.partNNN.csv` na expansão de colunas | Amostras antigas preservadas e FPS incluído no segmento novo |
| A16 | Nulos removidos antes de acessar gráficos/templates | Normalização preserva tema/intervalo e remove entradas inválidas |
| A17 | Totais de rede somam deltas de bytes | Contador simulado com intervalo ocioso |
| A18 | Discos inteiros identificados por `/sys/block` | virtio, Xen, eMMC, SCSI longo e exclusão de partições/agregadores |
| A19 | Link `.deb` usa `../lib/phantom-monitor` | Pacote gerado e extraído no Ubuntu, link resolvido sem usrmerge |
| A20 | Autostart usa `APPIMAGE` persistente e respeita XDG | Resolução de caminhos e escape de `Exec` |
| A21 | Extração tar com .NET; argumentos de aplicação não passam por shell | Arquivo e diretórios com espaços/apóstrofos |

## Outras medidas de confiabilidade

- A fila de logs aceita 256 snapshots; saturação é contabilizada e registrada.
- O HTTP limita a 16 handlers e cinco segundos por escrita; excesso recebe 503.
- O updater exige SHA-256 fornecido pela API GitHub, rejeita divergências e não
  publica `Applying` antes de iniciar o processo auxiliar. Releases antigas sem
  digest exigem download/verificação manual.
- A documentação não afirma assinatura de binários: os scripts não assinam.
- Vulkan valida uso de color attachment, proteção, camadas e família da fila;
  serializa o contexto ImGui e usa fences por imagem antes de reutilizar comandos.
- Hooks Windows suportam `Present1`, preservam render targets/depth D3D11 e não
  liberam o argumento de uma thread remota que ainda pode estar em execução.
- O projeto de regressão está na solução. O runner imprime `SKIP` explicitamente
  para testes de outro sistema e acumula falhas com código de saída diferente de zero.
- Shell scripts permanecem LF via `.gitattributes`. O AppDir recebe o ícone na raiz;
  o pacote Debian inclui dependências de runtime e cria o diretório de saída.
- `tools/ReleasePackager` gera o tarball com modos POSIX explícitos (755 para o
  executável e scripts, 644 para dados), inclusive quando a release é criada no
  Windows. AppImage/deb corrigem o bit executável ao copiar o payload.

## Como verificar

```powershell
dotnet build PhantomMonitor.slnx -c Release
dotnet run --project tests/PhantomMonitor.OverlayRegressionTests -c Release
cmake -S src/PhantomVulkanLayer -B build/native -A x64 -DBUILD_TESTING=ON
cmake --build build/native --config Release
ctest --test-dir build/native -C Release --output-on-failure
```

Para x86, use outro diretório e `-A Win32`. A camada exige headers Vulkan,
sem a biblioteca de importação de 32/64 bits.

No Linux, remova `-A x64`, configure `-DCMAKE_BUILD_TYPE=Release` e execute
`sh tests/linux-regression.sh`. Para testes Vulkan sem EGL, use
`-DPHANTOM_BUILD_OPENGL=OFF`; o preload GLX/EGL é ativado por padrão e requer seus
pacotes de desenvolvimento. `dotnet test` sozinho não executa o runner console.

## Aplicação e limites

Verificações realizadas neste ciclo:

| Ambiente | Resultado |
| --- | --- |
| Solução .NET Release | Compilação sem erros nem avisos |
| Runner Windows | 25 PASS e 1 SKIP POSIX, em 26 cenários |
| Runner auto-contido no Ubuntu/WSL | 24 PASS e 2 SKIP Windows |
| C++ Windows x64/x86 | Hooks, launcher, camada Vulkan e dois testes CTest aprovados em cada arquitetura |
| C++ Ubuntu/WSL | Vulkan, preload OpenGL GLX/EGL e dois testes CTest aprovados |
| Atualizadores em diretórios isolados | Sucesso e rollback após falha, Windows e Linux |
| Vulkan real em processo auxiliar | Instância/dispositivo criados e command buffer inicializado sem a violação de acesso original |

No Ubuntu, os headers EGL ausentes foram baixados/extráidos apenas em `build/`,
sem instalação de pacotes no sistema. Os testes .NET foram publicados como
auto-contidos, pois o Ubuntu não tinha SDK .NET. Nenhum jogo ou instalação real
foi alterado pelos testes de atualização.

Reinstale a camada Linux para publicar telemetria v2. O app ainda lê v1, mas
informa que as métricas são aproximadas. Na troca automática do Windows, feche
jogos que estejam usando os hooks. Backups `.phantom-update-backup-*` são
conservados para recuperação; só os remova após verificar a instalação nova.

Builds e testes de protocolo/transação não certificam todos os jogos, APIs,
GPUs, compositores ou anti-cheats. Ainda é necessária validação real de D3D9–12,
Vulkan, GLX/EGL/WGL, resize e captura de jogos. Os testes de pacote usam binários
fictícios: verificam a estrutura e o fluxo sem instalar ou substituir o app real.
