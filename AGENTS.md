# Regras e Diretrizes do Projeto (AGENTS.md)

Este documento define regras obrigatórias de segurança, versionamento e desenvolvimento para qualquer agente ou desenvolvedor atuando neste repositório.

---

## 1. Segurança e Prevenção de Vazamento no Git (CRÍTICO)

### NUNCA Adicionar ou Commitar no Git:
1. **Artefatos de Compilação (.NET e C++):**
   - Pastas `bin/` e `obj/` (em qualquer nível de diretório).
   - Arquivos compilados: `*.dll`, `*.exe`, `*.lib`, `*.exp`, `*.obj`, `*.pdb`, `*.ilk`, `*.idb`.
   - *Exceção controlada:* `tools/PresentMon.exe` (ferramenta externa indispensável de terceiros já versionada).
2. **Logs e Telemetria do Sistema:**
   - Diretórios `logs/` ou `Logs/`.
   - Arquivos `*.log`, `*.csv` gerados por monitoramento de sensores ou ETW.
   - Snapshots de telemetria da máquina local, como `sensors_latest.json`.
3. **Segredos, Credenciais e Configurações Pessoais:**
   - Arquivos `.env`, `.env.*`.
   - Arquivos locais de configuração: `config*.local.json`, `*.local.json`.
   - Chaves privadas, certificados (`*.pem`, `*.key`, `*.pfx`), senhas ou tokens de API.
4. **Metadados e Caches de IDE/Sistema:**
   - Pastas `.vs/`, `.idea/`, arquivos `.suo`, `*.user`, `*.userosscache`.
   - Arquivos que exponham caminhos absolutos locais ou usuários do sistema (ex: `C:\Users\<Usuario>\...`).

---

## 2. Fluxo Obrigatório de Git e Commits

Antes de realizar qualquer commit ou push:
1. **Verificação Prévia:**
   - Executar sempre `git status` e conferir cuidadosamente a lista de arquivos alterados/não rastreados.
   - **NUNCA** usar comandos de inclusão forçada (`git add -f`) em arquivos ou pastas de build (`bin`, `obj`).
   - Se novos arquivos foram criados, garantir que estejam cobertos pelo `.gitignore` antes de dar `git add`.
2. **Padrão de Mensagens:**
   - Usar formato de *Conventional Commits*:
     - `feat:` para novas funcionalidades.
     - `fix:` para correções de bugs.
     - `chore:` para manutenção, dependências e configurações.
     - `refactor:` para refatorações que não alteram comportamento.
     - `docs:` para documentação.

---

## 3. Estrutura do Projeto e Compilação

* **`src/RivaMonitor/`**: Aplicação desktop WPF em .NET 10.
* **`src/RivaSensorLib/`**: Biblioteca nativa em C++ para leitura de sensores de baixo nível.
* **`tools/`**: Ferramentas auxiliares externas (ex.: `PresentMon.exe`).
* **`docs/`**: Documentação de arquitetura e funcionalidades.

### Flexibilidade de Build e Geração de Executáveis:
* **Não travar o local de saída do executável:** Não restrinja nem engesse onde os executáveis ou builds são gerados (`bin/`, `publish/`, `dist/`, pasta customizada, etc.). O local de compilação, teste ou publicação é livre.
* **Regra única sobre binários:** Independentemente de onde o executável ou DLL for compilado/gerado, apenas garanta que esses binários não sejam commitados no repositório Git (já cobertos pelas regras globais do `.gitignore`).

