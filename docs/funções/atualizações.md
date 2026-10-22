# Atualizações (GitHub Releases)

> **Idioma:** Português (Brasil) · [English](../en/functions/updates.md)

O Phantom Monitor verifica novas versões publicadas no GitHub Releases e avisa na interface. A checagem é compartilhada pelos dois frontends (implementada em `PhantomMonitor.Services.Update`, no Core); a aplicação automática da atualização (download + troca de arquivos) é específica por plataforma.

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
- **Banner "Nova versão disponível"** no conteúdo principal quando existe versão mais nova, com botão "Ver no GitHub".
- **Badge "Nova versão"** no sidebar, ao lado do estado do monitor, clicável (abre a página de releases).

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
