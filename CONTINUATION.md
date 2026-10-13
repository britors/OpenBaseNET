# Ponto de retomada — 2026-09-27

O usuário autorizou commit, push e merge. A task #12 foi integrada e encerrada. Próxima implementação: #13 (geradores v2).

## Repositórios e PRs

- OpenBaseNET: `/home/rodrigo/Git/britors/OpenBaseNET`, main em `c890607e249ae304f8c911b7f6fd75c93b5fc7aa`.
- PR #18 (Oracle, `36b1944`) e PR #19 (template único, `c890607`) já integrados. Issues #2/#4/#6/#11 fechadas; M2 concluído; plano #7 atualizado.
- Task atual: https://github.com/britors/OpenBaseNET/issues/12
- **PR do CLI: https://github.com/britors/OpenBase.CLI/pull/159**, MERGED em 2026-09-27 22:26:45 UTC. Merge: `021fa9b121addc33756c521ad2998a04ebef0018`.
- CLI principal: `/home/rodrigo/Git/britors/OpenBase.CLI`, main sincronizada com origin/main em `021fa9b`, sem alterações locais.
- Worktree: `/tmp/openbase-cli-unified`; branch `feat/unified-template`, sincronizada com origin.
- Último commit da branch integrada: `c361d39` (todos enviados). Base `64271e2`; checkpoint WIP inicial `f076887` preservado no histórico.
- Git do CLI não tem identidade configurada. Usar `git -c user.name='Rodrigo Brito' -c user.email='britors@users.noreply.github.com' commit ...` como no OpenBaseNET; não alterar global.
- Mesmo se /tmp for limpo, branch/commits permanecem no Git original e remoto; criar nova worktree para retomar.

## Implementação

- `new -n Nome -d postgres|sqlserver|oracle`, aliases PostgreSQL, opção legada -s com aviso, conflitos/repetições, output com espaços, proteção de destino, validação de nome, JSON e modo não interativo.
- Gera somente pelo pacote `w3ti.OpenBaseNET.Template` 11.x. Preflight SDK/template/dry-run, manifesto v2 validado, caminhos/projetos/provider coerentes. Não instala, conecta/importa banco, aplica migration ou exige licença.
- Configuração de desenvolvimento opcional em User Secrets no Api localizado pelo manifesto; segredos em JSON no stdin do filho, não argv/logs/appsettings/manifesto. Licenças antigas ignoradas com aviso sem valor.
- Executor dotnet com ArgumentList e cancelamento da árvore de processos; stdout JSON sem wrapping/banners; erros 2/3/4/130 e informação de pasta/etapa quando há saída parcial.
- install/update/version/history adaptados, NuGet v3 e SemVer, previews explícitas, pacote local, linha 11.x, downgrade explícito e histórico. Pacotes legados e CLI acessíveis por --type; update padrão não atualiza o CLI implicitamente.
- Guard impede geradores legados em projetos v2 até #13/#14. Não anuncia suporte completo do ecossistema.
- CLI preview 11.0.0-preview.1; SDK global latestFeature a partir de 10.0.300.
- README/help e bash/fish atualizados; wrappers respeitam output e não mudam diretório em JSON.
- CI empacota OpenBaseNET c890607 uma vez e valida o mesmo nupkg em Linux/Windows. Workflows de publicação existentes em main não foram alterados. O merge autorizado os acionou, publicando o CLI `11.0.0-preview.1`. O template unificado continua não publicado.

## Evidências e correções concluídas

- **511 testes CLI passaram localmente**, incluindo parser real, manifestos/caminhos e cancelamento de processo filho. Mesma suíte já passou no Linux/Windows da CI em revisões anteriores.
- E2E completo local passou: seleção interativa e Ctrl+C em PTY; arquivos CLI iguais a dotnet new (normalizando GUIDs); build e 66 testes por banco (198 no total); casos compartilhados de argumentos; erros; User Secrets; version/history; guard legado.
- O E2E completo passou em Linux e Windows na CI do commit e4d44b1, incluindo os casos compartilhados, User Secrets e histórico.
- Corrigido marcador Spectre `__default_command` quando falta valor em opção array (-d/-s); testes reais cobrem as quatro formas.
- Corrigida leitura de BOM UTF-8 no teste de User Secrets.
- Testes antigos de extensões e novo teste de manifesto passaram a usar separadores de plataforma.
- PTY remove CI/GITHUB_ACTIONS somente no processo que simula terminal de usuário; CI real continua não interativa.
- E2E compara diretórios com samefile (Windows expande caminhos 8.3) e prefixa índice nos destinos dos casos Postgres/POSTGRES para evitar colisão em filesystem case-insensitive.
- Build E2E desativa servidores persistentes/reuso de nós para liberar DLLs de pacotes temporários no Windows.
- SonarCloud identificou regex sem timeout e política implícita de cancelamento de pipes. Corrigidos no commit 5c202e4: regex NonBacktracking com timeout, leitura de pipes explicitamente CancellationToken.None para drenar após matar filho. **SonarCloud e Quality Gate passaram nessa revisão.** Restam code smells não bloqueantes (não foram ocultados/suprimidos).
- Bash e fish: quatro verificações manuais cada, com nome/output com espaços/forma =/JSON, passaram sem stderr.

## Estado final / próxima ação

- Commit adicional `c361d39` preservou o check obrigatório `build-and-test`, agregando o empacotamento e os testes de Linux/Windows. A proteção da main não foi alterada; nenhum bypass foi usado.
- Todos os checks do head final `c361d391029960c6711da13ce3237c4554d0dbad` passaram antes do merge: CI https://github.com/britors/OpenBase.CLI/actions/runs/36355074149, CodeQL e SonarCloud.
- PR #159 integrado via squash em `021fa9b`; task https://github.com/britors/OpenBaseNET/issues/12 fechada automaticamente.
- Plano #7 atualizado: #12 concluída, próxima implementação #13, depois #14; M3 ainda não concluído.
- Os dois workflows existentes de publicação e CodeQL pós-merge passaram. Publicação confirmada de `w3ti.OpenBase.CLI` **11.0.0-preview.1** (HTTP Created, “Your package was pushed”): https://github.com/britors/OpenBase.CLI/actions/runs/36355313414. Outro workflow: https://github.com/britors/OpenBase.CLI/actions/runs/36355313423.
- Não houve publicação do pacote unificado de template. Geradores do layout v2 continuam bloqueados até a implementação #13/#14; CLI publicado é preview.
- Main local do CLI atualizada com fast-forward e limpa. Worktree da branch integrada mantida em `/tmp/openbase-cli-unified`, limpa, para referência.

Não resta ação de commit/push/merge da task #12. Se o usuário pedir para seguir, começar a task #13 com base na main atualizada do OpenBase.CLI; preservar alterações do usuário e consultar a issue/contratos antes de implementar.

Arquivos auxiliares: `/tmp/openbase-cli-merge-pr.md`, `/tmp/openbase-cli-merge-issue12-update.json`, `/tmp/openbase-cli-merge-plan7-update.json`, `/tmp/openbase-cli-merged-publication.log`. Reconsultar remoto antes de novas alterações.

## Executar validações

```bash
cd /tmp/openbase-cli-unified
dotnet test --no-restore -c Release --verbosity minimal
python3 scripts/check-unified-template.py \
  --package /home/rodrigo/Git/britors/OpenBaseNET/artifacts/packages/w3ti.OpenBaseNET.Template.11.0.0-preview.1.nupkg \
  --template-root /home/rodrigo/Git/britors/OpenBaseNET
```

Testes/MSBuild exigem escalada por pipes/network do sandbox. E2E usa DOTNET_CLI_HOME/APPDATA/OPENBASE_STATE_HOME temporários, sem redefinir HOME. Se faltar pacote: executar `python3 scripts/pack-template.py --version 11.0.0-preview.1` no OpenBaseNET. Contratos em `docs/contracts` e `contracts` do OpenBaseNET.

Não usar subagentes sem pedido explícito. Não interferir em processos de outros projetos do usuário. Este arquivo é memória local não versionada, não alteração do produto.
