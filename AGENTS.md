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

* **`src/PhantomMonitor.App/`**: Aplicação desktop única em Avalonia/.NET 10 (Windows e Linux).
* **`src/PhantomMonitor.Core/`**: Modelos, configuração, avaliação de alertas e serviços compartilhados em .NET (sensores em `Services/Sensors/Windows/` e `Services/Sensors/Linux/`, integrações em `Services/Platform/`, updater em `Services/Update/`).
* **`src/PhantomSensorLib/`**: Biblioteca nativa em C++ para leitura de sensores de baixo nível.
* **`src/PhantomVulkanLayer/`**: Camada Vulkan nativa em C++ para Linux.
* **`scripts/`**: Automação local (ex.: `release.ps1`, empacotamento de releases win/linux).
* **`packaging/`**: Empacotadores (`windows/phantom-monitor.iss` com Inno Setup; `linux/appimage.sh` e `linux/deb.sh`).
* **`tools/`**: Ferramentas auxiliares externas (ex.: `PresentMon.exe`).
* **`docs/`**: Documentação de arquitetura e funcionalidades.

### Flexibilidade de Build e Geração de Executáveis:
* **Não travar o local de saída do executável:** Não restrinja nem engesse onde os executáveis ou builds são gerados (`bin/`, `publish/`, `dist/`, pasta customizada, etc.). O local de compilação, teste ou publicação é livre.
* **Regra única sobre binários:** Independentemente de onde o executável ou DLL for compilado/gerado, apenas garanta que esses binários não sejam commitados no repositório Git (já cobertos pelas regras globais do `.gitignore`).

---

## 4. Manutenção Contínua da Documentação (OBRIGATÓRIO)

Sempre que novas funcionalidades, alterações arquiteturais, novos sensores, configurações ou refatorações forem implementadas:
1. **Sincronização Imediata:** A documentação técnica deve ser mantida rigorosamente atualizada no mesmo ciclo de desenvolvimento da alteração de código.
2. **Diretório `docs/`:** Atualizar ou criar os documentos correspondentes na pasta `docs/` (ex.: `docs/funções/` e guias de módulos).
3. **`README.md`:** Refletir mudanças em instruções de execução, novos requisitos, atalhos, arquitetura geral ou dependências.
4. **Sem Código Órfão de Documentação:** Nenhuma funcionalidade deve ser adicionada ou modificada sem que a respectiva documentação reflita o comportamento real e atualizado do sistema.

---

## 5. Modularidade e Organização da Base de Código (Proatividade)

1. **Criação Livre de Arquivos e Pastas:** Sempre que necessário para manter a clareza, legibilidade, desacoplamento e boa organização do projeto, **crie novos arquivos ou pastas sem hesitar**.
2. **Evitar Arquivos Monolíticos:** Não concentre múltiplas responsabilidades em arquivos extensos. Divida classes, serviços, utilitários, componentes de interface ou documentação em módulos dedicados com responsabilidades bem definidas (Single Responsibility Principle).
3. **Consistência Estrutural:** Posicione os novos arquivos respeitando a estrutura do projeto (ex.: regras de negócio/serviços em `Services/`, entidades em `Models/`, novas janelas/views em diretórios temáticos e novos manuais em `docs/`).
