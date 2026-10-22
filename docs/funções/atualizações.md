# Atualizações (GitHub Releases)

> **Idioma:** Português (Brasil) · [English](../en/functions/updates.md)

O Phantom Monitor verifica novas versões publicadas no GitHub Releases, avisa na interface e, no Windows e no Linux (portátil/AppImage), chega a baixar, trocar os arquivos e reabrir sozinho com o botão "Atualizar e reiniciar". A checagem é compartilhada pelos dois frontends (implementada em `PhantomMonitor.Services.Update`, no Core); a aplicação da atualização é específica por plataforma (appliers).

## Como funciona

- A fonte é a API pública do GitHub (sem autenticação). Por padrão o repositório é `BadTonho/PhantomMonitor`; `update.repository` no config pode apontar para outro (`owner/repo` ou URL do GitHub).
- O app compara a **tag** da release com a versão compilada no executável (`<Version>` do csproj no publish; ex.: tag `v1.2.3` ↔ versão `1.2.3`). Versões mais antigas ou iguais nunca são "instaladas" (não há rebaixamento).
- Resposta 404 ou lista vazia também é tratada como "nenhuma release publicada ainda", não como erro.

## Cadência

1. **No arranque** (quando o toggle de checagem automática está ligado);
2. **A cada 24h** enquanto a janela principal fica aberta;
3. **Botão "Buscar agora"** no card de atualizações, quando quiser.

Falhas de rede/limite da API são registradas em `logs/errors.log` e mostradas como mensagem curta no card — o app não abre modal nem interrompe o monitoramento.

## Interface (Windows, WPF)

- **Card "Atualizações"** (fim da página): versão instalada, última checagem, botão "Buscar agora", botão que abre a página de releases no navegador e os toggles de checagem automática e canal beta.
- **Banner "Nova versão disponível"** no conteúdo principal quando existe versão mais nova, com botões "Ver no GitHub" e, quando a troca é possível, **"Atualizar e reiniciar"** (mostra "Baixando: N %" durante o download e "Reiniciando..." na troca).
- **Badge "Nova versão"** no sidebar, ao lado do estado do monitor, clicável (abre a página de releases).

## Interface (Linux, Avalonia)

- **Card "ATUALIZAÇÕES (GITHUB RELEASES)"** na página Configurações: toggles de checagem automática e canal beta, campo "Repositório" (vazio usa o oficial), versão instalada, última checagem e os botões "Buscar agora", "Abrir página de releases" e — quando a troca é possível — "Atualizar e reiniciar". As preferências entram no fluxo do botão "Salvar e aplicar" da própria página (validação: `owner/repo` ou URL do GitHub).
- **Toast "PHANTOM MONITOR · ATUALIZAÇÃO"** no canto direito do dashboard quando existe versão nova ou o download/troca está em curso, com os mesmos botões.
- **Badge de atualização** no box ESTADO da sidebar (chip verde discreto): mostra "Nova versão: <tag>", "Baixando: N %" ou "Pronto para reiniciar" conforme o estado; clicável e abre a página Configurações (F1.2 da unificação visual, paridade do WPF).
- Em build de desenvolvimento (`0.0.0-dev`), o card indica que a verificação está desactivada.

## Aplicar automaticamente (Windows)

O botão "Atualizar e reiniciar" executa o ciclo completo:

1. Baixa o asset da convenção `PhantomMonitor-<versão>-win-x64.zip` (portable zip — é o mesmo payload usado por quem instalou via setup: em ambos os casos os arquivos juntos se trocam pela pasta).
2. Descompacta em `%LOCALAPPDATA%\PhantomMonitor\UpdateStaging\<versão>\` (cleanup de stagings antigos antes de cada preparação).
3. Lança o updater one-shot (`apply_update.cmd` gerado no staging): espera o processo do app encerrar (até 30 s), troca os arquivos com **backup** (`*.bak`), reabre o executável e apaga os backups. Em falha de cópia o `.bak` é restaurado na hora — nunca há relançamento com arquivos pela metade.
4. O app é encerrado e reaberto com a versão nova. O config do usuário (`%APPDATA%\PhantomMonitor\config.json`) e os logs ficam fora da pasta trocada, intocados.

Limites honestos:

- Antes da troca o app grava um probe na própria pasta de instalação; se ela for protegida (portable extraído em `Program Files`, por exemplo), a troca não é oferecida e só fica o modo notificação com link.
- Se a release não tiver o asset do formato esperado, o botão não aparece e o card explica o fator ("esta release não tem o arquivo esperado; baixe manualmente").
- O executável é fechado durante a troca: o overlay e o monitoramento param e voltam quando o app reabre.

## Aplicar automaticamente (Linux)

No Linux o fluxo é detectado no arranque em dois formatos:

- **Portátil (tarball)** — asset `PhantomMonitor-<versão>-linux-x64.tar.gz`: baixa, extrai com o `tar` do sistema em `$XDG_DATA_HOME/PhantomMonitor/UpdateStaging/<versão>/` e o one-shot `apply_update.sh` espera o processo encerrar (até 60 s, `kill -0`), troca os arquivos com backups (`*.bak`) — restaurados em falha de cópia — e reabre o executável com `nohup` quando a troca completa.
- **AppImage** (`$APPIMAGE` definido) — asset `PhantomMonitor-<versão>-linux-x64.AppImage`: o script apenas substitui o arquivo do AppImage, dá `chmod +x` e o reabre.

Limites e fallbacks:

- A instalação é testada com um probe de gravação (AppImage: pasta que contém o arquivo; portátil: pasta de instalação). Quando não é possível trocar — instalação por `.deb` ou outro pacote do sistema em diretório protegido — o botão não aparece e o app fica no modo notificação com link; a atualização é feita pelo gerenciador de pacotes.
- O config do usuário (`~/.config/PhantomMonitor/config.json`) e os logs ficam fora da pasta trocada, intocados.
- A camada Vulkan não participa da troca de arquivos do aplicativo: após atualizar, reinstale-a com o `install_layer.sh` da versão nova (ver [suporte-linux.md](suporte-linux.md)).

## Convenção de assets e publicação (mantenedor)

As releases são geradas localmente (sem CI de publicação); o updater procura os nomes exatos abaixo:

| Asset | Uso |
|---|---|
| `PhantomMonitor-<versão>-win-x64.zip` | Automático (Applier Windows) + portable |
| `PhantomMonitor-<versão>-linux-x64.tar.gz` | Automático (Applier Linux portátil) + portable |
| `PhantomMonitor-<versão>-linux-x64.AppImage` | Automático (Applier do AppImage) |
| `PhantomMonitorSetup-<versão>.exe` | Instalador per-user (Inno Setup; cada arquivo junto à pasta trocada) |
| `phantom-monitor_<versão>_amd64.deb` | Instalação .deb (fica em modo notificação) |
| `PhantomVulkanLayer-<versão>-linux.so` | Camada Vulkan (instalação manual; atualiza reexecutando o `install_layer.sh` da versão) |

Fluxo de publicação:

1. `scripts/release.ps1 -Version <x.y.z>`: publica os dois frontends self-contained single-file com `-p:Version=<versão>` e gera o zip e o tarball em `releases/<versão>/`. Opções: `-PackWindowsSetup` (instalador com o ISCC quando o Inno Setup 6 está presente), `-LayerSoPath <so>` (anexa a camada Vulkan compilada), `-SkipWindows/-SkipLinux` e `-OutDirectory`.
2. No Linux, os empacotadores experimentais (NÃO TESTADOS) ficam em `packaging/linux/`: `appimage.sh` (appimagetool) e `deb.sh` (dpkg-deb). O instalador Windows em `packaging/windows/phantom-monitor.iss`.
3. Revise os pacotes, crie a tag `v<x.y.z>` (`git tag v1.2.3 && git push --tags`) e publique: o script imprime o comando `gh release create v1.2.3 ...` com os assets — ou suba tudo pela página do GitHub.
4. Rascunho (draft) não conta como atualização disponível; a tag deve ser a versão mesma (ex.: `v1.2.3` ↔ `1.2.3` comparada pelo SemVer além do "v").

## Canais

- **Estável** (padrão): usa `GET /releases/latest`, que já ignora rascunhos e pré-releases.
- **Canal beta** (toggle): consulta `GET /releases` e considera a release mais nova não-rascunho, **inclusive pré-releases** marcadas no GitHub; o banner indica "(pré-release, canal beta)".

Trocar o canal não reavalia sozinho: use "Buscar agora" para refazer a checagem no canal novo.

## Builds de desenvolvimento

Builds locais (`dotnet build`) trazem o marcador `0.0.0-dev`; eles **nunca** checam nem atualizam, de propósito (a própria execução na pasta do repositório não é atualizável). A versão real entra no publish da release (`-p:Version=<tag>`).

## Configuração

```json
"update": {
  "autoCheck": true,
  "checkBeta": false,
  "repository": ""
}
```

- `autoCheck` — checagem no arranque + timer de 24h (o botão manual funciona sempre). O toggle persiste no config.
- `checkBeta` — canal beta (acima).
- `repository` — vazio usa o repositório oficial; aceita `owner/repo` ou URL do GitHub (incluindo sufixo `.git`).

Ver [configuracao.md](configuracao.md) para todo o schema do config.
