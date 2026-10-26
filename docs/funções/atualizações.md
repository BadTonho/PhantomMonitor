# Atualizações (GitHub Releases)

> **Idioma:** Português (Brasil) · [English](../en/functions/updates.md)

O Phantom Monitor verifica novas versões publicadas no GitHub Releases, avisa na interface e, no Windows e no Linux (portátil/AppImage), chega a baixar, trocar os arquivos e reabrir sozinho com o botão "Atualizar e reiniciar". A checagem é compartilhada pelas plataformas (implementada em `PhantomMonitor.Services.Update`, no Core); a aplicação da atualização é específica por plataforma (appliers).

## Como funciona

- A fonte é a API pública do GitHub (sem autenticação). Por padrão o repositório é `BadTonho/PhantomMonitor`; `update.repository` no config pode apontar para outro (`owner/repo` ou URL do GitHub).
- O app compara a **tag** da release com a versão compilada no executável (`<Version>` do csproj no publish; ex.: tag `v1.2.3` ↔ versão `1.2.3`). Versões mais antigas ou iguais nunca são "instaladas" (não há rebaixamento).
- Resposta 404 ou lista vazia também é tratada como "nenhuma release publicada ainda", não como erro.

## Cadência

1. **No arranque** (quando o toggle de checagem automática está ligado);
2. **A cada 24h** enquanto a janela principal fica aberta;
3. **Botão "Buscar agora"** no card de atualizações, quando quiser.

Falhas de rede/limite da API são registradas em `logs/errors.log` e mostradas como mensagem curta no card — o app não abre modal nem interrompe o monitoramento.

## Interface (app único)

Os mesmos elementos servem no Windows e no Linux (os detalhes do WPF foram portados na unificação visual):

- **Toast "PHANTOM MONITOR · ATUALIZAÇÃO"** no canto direito do dashboard quando existe versão nova ou o download/troca está em curso, com os mesmos botões (no WPF era o banner "Nova versão disponível" no conteúdo principal, com "Ver no GitHub" e **"Atualizar e reiniciar"** — mostra "Baixando: N %" durante o download e "Reiniciando..." na troca).
- **Badge de atualização** no box ESTADO da sidebar (chip verde discreto): mostra "Nova versão: <tag>", "Baixando: N %" ou "Pronto para reiniciar" conforme o estado; clicável e abre a página Configurações (F1.2 da unificação visual, paridade do WPF).
- **Card "ATUALIZAÇÕES (GITHUB RELEASES)"** na página Configurações: toggles de checagem automática e canal beta, campo "Repositório" (vazio usa o oficial), versão instalada, última checagem e os botões "Buscar agora", "Abrir página de releases" e — quando a troca é possível — "Atualizar e reiniciar". As preferências entram no fluxo do botão "Salvar e aplicar" da própria página (validação: `owner/repo` ou URL do GitHub).
- Em build de desenvolvimento (`0.0.0-dev`), o card indica que a verificação está desativada.

## Aplicação transacional

O download exige o campo `digest` SHA-256 da API GitHub e rejeita conteúdo
divergente ou releases sem digest. A confiança continua vinculada ao repositório
e HTTPS; checksum não é assinatura do mantenedor.

Windows extrai todo o ZIP para o diretório da versão e executa `apply_update.ps1`
com o PowerShell do sistema. Antes de encerrar, verifica DLLs nativas em uso;
feche os jogos que usam o hook. O script espera até 60 segundos pela saída do app,
prepara cópias completas e atualiza os arquivos de todas as subpastas, incluindo
`PhantomOverlay/x86` e `PhantomOverlay/x64`. Em falha tratada, desfaz todas as
alterações anteriores, remove arquivos novos e restaura os originais.

No Linux, o tarball é extraído pelo .NET sem shell ou requisito de `tar` externo.
AppImage é baixado no mesmo diretório de versão usado pelo aplicador. O script
POSIX prepara o payload antes da troca, restaura todas as entradas modificadas
se um movimento falhar e substitui AppImage por rename de arquivo preparado no
mesmo filesystem. Caminhos com espaços/apóstrofos são suportados.

Backups `.phantom-update-backup-*` são conservados para recuperação manual. No
Windows, `status` e `journal.json` descrevem a transação; `original/` contém os
arquivos antigos. No portable Linux, `original.list`/`installed.list` e `original/`
descrevem as entradas. Só remova backups após verificar a nova instalação, com o
aplicativo fechado. Falta de energia ou falha no próprio rollback pode exigir
recuperação manual; a cópia antiga não é apagada antecipadamente.

A UI só publica `Applying` depois de iniciar o atualizador com sucesso. Config
e logs ficam fora da instalação. Pacotes de sistema/pastas protegidas continuam
em modo de notificação; a camada Linux deve ser reinstalada separadamente.

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

1. `scripts/release.ps1 -Version <x.y.z>`: publica o app único (win-x64 e linux-x64) self-contained single-file com `-p:Version=<versão>` e gera o zip e o tarball em `releases/<versão>/` — o payload do Windows é `PhantomMonitor.exe` + `PresentMon.exe` + `config.example.json` (o script garante o PresentMon solto e limpa os `*.pdb`) e o do Linux é o binário plus o hardlink transicional. Opções: `-PackWindowsSetup` (instalador com o ISCC quando o Inno Setup 6 está presente), `-LayerSoPath <so>` (anexa a camada Vulkan compilada), `-SkipWindows/-SkipLinux` e `-OutDirectory`.
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
