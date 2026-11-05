<p align="right"><a href="README.md">English</a> | Português (Brasil)</p>

<p align="center">
  <img src="fantasma.png" alt="Ícone provisório do Phantom Monitor" width="170" />
</p>

<h1 align="center">Phantom Monitor</h1>

<p align="center">
  <strong>Hardware, FPS e frametime em um só lugar.</strong><br />
  Monitoramento local para Windows e Linux, com dashboard e overlay configurável.
</p>

<p align="center">
  <img src="https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white" alt=".NET 10" />
  <img src="https://img.shields.io/badge/Windows-Avalonia-0078D4?logo=windows&logoColor=white" alt="Windows com Avalonia" />
  <img src="https://img.shields.io/badge/Linux-Avalonia-FCC624?logo=linux&logoColor=111111" alt="Linux com Avalonia" />
</p>

<p align="center">
  <a href="#recursos">Recursos</a> ·
  <a href="#comece-a-usar">Comece a usar</a> ·
  <a href="#arquitetura">Arquitetura</a> ·
  <a href="#documentacao">Documentação</a>
</p>

O **Phantom Monitor** acompanha sensores do computador e apresenta as leituras em tempo real. Ele reúne uso, temperaturas, clocks, memória, armazenamento, rede, ventoinhas e telemetria de jogos em uma interface desktop.

O projeto roda um **único app desktop Avalonia** no Windows e no Linux. Releases anteriores tinham um frontend WPF no Windows, aposentado na fase 2 do [roadmap de unificação visual](docs/roadmap-unificacao-visual.md); a tabela abaixo resume o suporte atual por plataforma.

## Recursos

| Recurso | Windows | Linux |
| --- | --- | --- |
| Sensores | CPU, GPU, RAM, discos, rede, ventoinhas e sensores da placa-mãe via LibreHardwareMonitor | Sensores de `sysfs`, `procfs` (incluindo `fdinfo` DRM), `hwmon`, zonas térmicas Linux, DRM e NVML opcional da NVIDIA; métricas de GPU variam conforme o driver |
| FPS e frametime | PresentMon via ETW | X11 Present em janelas X11/XWayland e captura passiva de quadros Vulkan e OpenGL GLX/EGL |
| Overlay | OSD flutuante com click-through e perfis; renderizadores opcionais dentro do jogo para executáveis x86/x64 autorizados: Direct3D 9–12, OpenGL/WGL e Vulkan ao iniciar pelo PhantomMonitor | Janelas separadas e arrastáveis; renderização opcional dentro do jogo via Vulkan e OpenGL GLX/EGL; os pacotes Linux incluem os dois renderizadores nativos |
| Inventário | Inventário de hardware e estatísticas por série | Séries detectadas agrupadas por categoria; sensores sem leitura aparecem como indisponíveis |
| Alertas | Notificação, som, scripts e desligamento configurável | Toast interno, notificações do desktop e execução de scripts |
| Diagnóstico | Fontes de sensores, avisos atuais de GPU e log de erros | Fontes por placa, geometria das janelas do overlay, avisos atuais de GPU, erros recentes e detalhes da NVML NVIDIA |
| Fórmulas e ajuste por série | Compatibilidade com as configurações existentes | Editores visuais de sensores customizados e ajuste de cadência/suavização |
| Telemetria | CSV, JSON, JSONL e exportador HTTP opcional | CSV, JSON, JSONL e exportador HTTP opcional |

O suporte varia conforme hardware, API gráfica e compositor. Os pacotes Linux incluem a camada Vulkan e o preload GLX/EGL. O modo flutuante prepara a captura passiva de quadros sem desenhar no jogo; **Dentro do jogo** também habilita o renderer do OSD. Ambos dependem do ambiente de lançamento, então jogos e launchers já abertos precisam ser reiniciados após a ativação. No KDE Plasma, o app tenta atualizar o ambiente pelo serviço de sessão e, quando disponível, pelo KLauncher. O diagnóstico informa se algum serviço confirmou a atualização; se nenhum confirmar, jogos iniciados pelo menu podem não receber o hook nesta sessão. X11 Present continua disponível para janelas X11/XWayland; janelas Wayland nativas dependem de hook Vulkan ou OpenGL carregado no processo. O utilitário independente `phantom-hud` (em `~/.local/bin/phantom-hud`) permite iniciar jogos e launchers diretamente (ex.: `phantom-hud %command%` na Steam ou como comando de empacotador no Prism Launcher, MultiMC, Heroic e Lutris). Flatpak pode exigir integração própria. No Windows, o modo dentro do jogo detecta aplicações apresentando frames com ETW/PresentMon e tenta instalar hooks nativos; pode exigir a associação ao grupo Performance Log Users. Anti-cheats podem bloqueá-los e o Phantom Monitor não tentará contornar essas proteções. Os renderizadores nativos Windows exigem build em Windows com Visual Studio 2022 e Vulkan SDK e ainda precisam de validação em execução no Windows.

## Comece a usar

É necessário ter o **SDK .NET 10** instalado. Consulte os requisitos detalhados em [Suporte a Linux](docs/funções/suporte-linux.md) e [Configuração](docs/funções/configuracao.md).

### Linux

Inicie o dashboard Avalonia:

```bash
dotnet run --project src/PhantomMonitor.App
```

Para um build de desenvolvimento local, compile os renderizadores nativos na pasta de saída do app:

```bash
scripts/build-linux-overlay.sh src/PhantomMonitor.App/bin/Debug/net10.0/PhantomOverlay/linux
dotnet run --project src/PhantomMonitor.App
```

Os pacotes de release já incluem os renderizadores Vulkan e OpenGL. No Linux, a captura de quadros é preparada nos dois modos do overlay. O PhantomMonitor registra a camada Vulkan para telemetria passiva e publica o preload GLX/EGL no ambiente D-Bus/systemd; no KDE Plasma, também tenta atualizar o ambiente de lançamento pelos serviços disponíveis na sessão. Se quiser desenhar o OSD no jogo, selecione **Dentro do jogo**. O diagnóstico informa se o desktop confirmou a atualização; se não confirmar, jogos iniciados pelo menu podem não receber os hooks nesta sessão. Feche e reabra jogos ou launchers que já estavam abertos. No Windows, a lista de executáveis continua disponível para iniciar jogos com parâmetros específicos.

Para gerar uma release, compile o payload Linux em uma máquina Linux, disponibilize a pasta na máquina Windows de release e informe o caminho ao script:

```bash
scripts/build-linux-overlay.sh build/linux-overlay-payload
```

Na máquina Windows de release, depois de disponibilizar essa pasta:

```powershell
powershell -File scripts/release.ps1 -Version 1.2.3 -LinuxOverlayDirectory .\build\linux-overlay-payload
```

Vulkan e OpenGL GLX/EGL fornecem telemetria passiva de FPS no modo flutuante e podem desenhar o OSD dentro do jogo em `inGame`. O desenho OpenGL suporta EGL (OpenGL ES 3+) e um contexto por processo. Em **Configurações → Modo do overlay**, `floating` continua sendo o padrão e configurações antigas migram para esse valor. Escolher `inGame` oculta as janelas flutuantes; o estado e as falhas nativas por processo aparecem em Diagnóstico. Vários swapchains Vulkan ou DXGI no mesmo processo são indicados como não suportados. Em um desktop Wayland, confira **Diagnóstico → Resumo** para ver o backend Avalonia: a configuração padrão do projeto usa X11/XWayland, enquanto um compositor Wayland nativo pode ignorar posições absolutas das janelas.

### Windows

Inicie o app:

```powershell
dotnet run --project src/PhantomMonitor.App
```

O atalho para mostrar ou ocultar o overlay pode ser definido em **Configurações → Modo do overlay**; ele vem desativado. O suporte a X11 e Wayland ainda aguarda validação no Linux. Veja [atalhos globais](docs/funções/hotkey.md).

No editor do overlay, **Adicionar conteúdo → Adicionar linha personalizada** permite combinar texto livre com nomes detectados da CPU, GPU e placa-mãe, ajustando fonte e largura por linha. Veja [overlay](docs/funções/overlay.md).

O tema builtin é escolhido com `theme` no `config.json` (`neptune` = paleta derivada do Avalonia, `slate` = paleta derivada do WPF; a chave legada `uiTheme` continua sendo lida e migra), pelo card **INTERFACE** em Configurações (trocado na interface em execução, sem reiniciar) ou pela linha de comando com `--theme=<nome>` (o `--ui-theme=` legado segue aceito).

O card **INTERFACE** também alterna a navegação entre a lateral esquerda, a barra horizontal no rodapé e um dock flutuante centralizado sobre o conteúdo, dentro da janela. É possível mostrar ícones e textos ou somente ícones; na lateral compacta, a coluna reduz para 76 px. As escolhas são salvas imediatamente em `navigationPosition` (`left` por padrão) e `navigationDisplayMode` (`iconsAndText` por padrão), inclusive em configurações antigas.

Algumas leituras de hardware podem exigir permissões elevadas. O FPS usa PresentMon/ETW e pode depender da versão do Windows e das permissões disponíveis.

O `dotnet build` e o `dotnet publish` comuns não compilam nem copiam o renderer
Windows dentro do jogo. Para um build local funcional, execute
`scripts/build-windows-overlay.ps1 -OutDirectory <pasta-do-app>/PhantomOverlay`;
o script de release já faz isso automaticamente. Ele gera hooks, launchers e
camadas Vulkan x86/x64 e exige as ferramentas C++ do Visual Studio 2022 e o Vulkan SDK.
Ainda é necessária validação em execução no Windows para cada API e game capture.

## Releases e atualização automática

Builds publicados ficam na [página de Releases](https://github.com/BadTonho/PhantomMonitor/releases). O app checa novas versões no arranque e a cada 24h e oferece "Atualizar e reiniciar": baixa o pacote da plataforma, troca os arquivos via atualizador one-shot (com backup/rollback) e reabre sozinho. A configuração portátil é preservada durante a troca; logs continuam na pasta de dados do usuário. Instalação por pacote de sistema (.deb) e pastas protegidas ficam em modo notificação com link. Cada release segue a convenção:

| Asset | Plataforma |
| --- | --- |
| `PhantomMonitor-<versão>-win-x64.zip` | Windows (portável; também é o payload do instalador) |
| `PhantomMonitor-<versão>-linux-x64.tar.gz` | Linux (portável) |
| `PhantomMonitor-<versão>-linux-x64.AppImage` | Linux (AppImage) |
| `PhantomMonitorSetup-<versão>.exe` | Windows (instalador per-user via Inno Setup) |
| `phantom-monitor_<versão>_amd64.deb` | Linux (pacote .deb) |
Releases são geradas localmente por mantenedores (`scripts/release.ps1`, sem CI); o tarball Linux inclui os dois renderizadores nativos. Veja a [documentação de atualizações](docs/funções/atualizações.md) e os empacotadores em `packaging/`. Os scripts não assinam os binários. O Windows pode exibir o aviso do **SmartScreen** ("editor desconhecido") e o Linux pode pedir confirmação na primeira abertura. O download automático exige o SHA-256 publicado pela API do GitHub; a troca inclui subpastas, conserva backups de recuperação e restaura o conjunto alterado em falhas de cópia tratadas. Feche jogos que usam os hooks Windows antes de atualizar.

## Validação das correções

O tarball Linux usa `tools/ReleasePackager` para gravar permissões POSIX explícitas,
inclusive quando a release é empacotada no Windows.

Execute `dotnet test` (ou `dotnet test tests/PhantomMonitor.OverlayRegressionTests -c Release`) para rodar a suíte xUnit de regressão e testes unitários (`RegressionTests`, `AlertRuleEvaluatorTests`, `CustomSensorFormulaParserTests` e `AppConfigNormalizeTests`). Para C++, use `cmake -S src/PhantomVulkanLayer -B build/native -DBUILD_TESTING=ON`, `cmake --build build/native --config Release` e `ctest --test-dir build/native -C Release --output-on-failure`. No Linux, `sh tests/linux-regression.sh` verifica atualização, rollback e pacote `.deb`.

A camada Vulkan resolve funções dinamicamente e exige apenas os headers do SDK, sem biblioteca de importação de 32 ou 64 bits. O preload OpenGL exige os pacotes de desenvolvimento EGL/GLX; `-DPHANTOM_BUILD_OPENGL=OFF` permite compilar Vulkan e seus testes independentemente. Os pacotes Linux atualizam as bibliotecas nativas junto com o app; instalações manuais feitas por `install_layer.sh` devem executar o script novamente após atualizar. No Windows, o lançamento antecipado está disponível em Configurações para capturar a fila exata de jogos D3D12 com múltiplas filas.

## Configuração e dados

Na primeira execução, o aplicativo cria `config.json` usando [`config.example.json`](config.example.json) como base. No ZIP portátil do Windows e no tar.gz portátil do Linux, ele fica junto do executável; no AppImage, junto do arquivo `.AppImage`. Leve esse arquivo com o aplicativo para manter suas configurações. A pasta precisa permitir gravação: se isso falhar, o aplicativo avisa e não muda silenciosamente de local. Um portátil atualizado sem configuração local começa com uma nova, sem copiar ou apagar a configuração antiga do computador.

Durante o desenvolvimento, a configuração fica na raiz do repositório. No instalador Windows e no pacote `.deb`, o caminho usual é `%AppData%\PhantomMonitor\config.json` no Windows e `$XDG_CONFIG_HOME/PhantomMonitor/config.json` no Linux. Os logs continuam nos locais de dados do usuário.

No Linux, os logs de telemetria e de erros ficam em `~/.local/share/PhantomMonitor/logs/` (ou em `$XDG_DATA_HOME` quando definido). Abra **Diagnóstico** no Avalonia para consultar erros recentes e as fontes de sensores detectadas. A página **Configurações** permite ajustar o intervalo, os formatos de log e opções do overlay. O exportador HTTP vem desativado e restrito a loopback; habilitar acesso a outras interfaces (`listenAll`) gera e exige um token de acesso (`Authorization: Bearer <token>` ou `?token=<token>`), embora o tráfego continue em HTTP sem TLS.

## Arquitetura

```text
src/
├── PhantomMonitor.App/   # App desktop unificado em Avalonia (Windows + Linux)
├── PhantomMonitor.Core/  # Modelos e serviços compartilhados em .NET
├── PhantomVulkanLayer/   # Camada Vulkan nativa para FPS no Linux
└── PhantomSensorLib/     # Biblioteca nativa C++ prevista para extensão futura
```

O Core compartilha modelos, configuração, avaliação de alertas e serviços de telemetria; o app mantém as integrações específicas de cada sistema operacional. Os renderizadores nativos Linux são compilados separadamente com CMake por `scripts/build-linux-overlay.sh` e incluídos nas releases; eles não fazem parte do build da solução .NET.

## Documentação

A documentação também está disponível em [English](docs/en/README.md); os originais em português estão organizados no [índice pt-BR](docs/README.md).

- **Plataformas:** [Suporte a Linux e camada Vulkan](docs/funções/suporte-linux.md), [FPS e frametime](docs/funções/fps-monitor.md)
- **Interface:** [Overlay](docs/funções/overlay.md), [Inventário de hardware](docs/funções/inventário-hardware.md)
- **Sensores:** [Sensores customizados](docs/funções/sensores-customizados.md), [Ajuste por série](docs/funções/ajuste-de-series.md)
- **Automação e saída:** [Alertas](docs/funções/alertas.md), [Logging](docs/funções/logging.md), [SDK de telemetria](docs/telemetry-sdk.md)
- **Projeto:** [Roadmap de paridade funcional](docs/roadmap-paridade.md), [Arquitetura e módulos](docs/funções/)

## Estado do projeto

O app é o **único frontend desktop Avalonia, tanto no Windows quanto no Linux**; os recursos disponíveis variam por sistema operacional (integrações de sensores, FPS e OSD). Consulte o [roadmap](docs/roadmap-paridade.md) para acompanhar os próximos passos por plataforma.

## Licença

O código original do Phantom Monitor está licenciado sob a GNU Lesser General Public License v3.0 somente (`LGPL-3.0-only`). Consulte [LICENSE](LICENSE). Componentes de terceiros mantêm suas próprias licenças; os avisos do executável PresentMon distribuído com o projeto estão em [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
