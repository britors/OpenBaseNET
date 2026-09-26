# Matriz de aceitação dos contratos

## M1 — Verificável agora

- Schema v2 valida os três exemplos e rejeita campos/versões inválidos.
- Providers são canônicos; aliases pertencem à entrada do CLI, não ao manifesto.
- Paths de manifesto não permitem navegação para fora da raiz na forma lexical.
- Envelope JSON distingue sucesso, falha e avisos com formato consistente.
- Exemplos legados não são aceitos silenciosamente como manifesto v2.
- Casos de criação cobrem aliases, conflitos, modo não interativo e o limite de `-d`.
- Documentação separa comportamento contratado de funcionalidades já implementadas.

`python -m unittest discover -s tests/contracts -v` é a verificação deste marco.
Ela não prova a implementação futura do CLI, do template ou dos adaptadores.

## M2 — Núcleo, template e bancos

O [núcleo Customer](../core.md) já tem testes de domínio, casos de uso e fronteiras sem
banco/HTTP. A matriz abaixo continua sendo o critério integrado dos adaptadores/template;
os doubles de unidade não contam como validação de SQL, transações reais ou geração.

| Verificação | PostgreSQL | SQL Server | Oracle |
| --- | --- | --- | --- |
| Gerar com nome novo, inclusive namespace com pontos | obrigatório | obrigatório | obrigatório |
| Quatro projetos e driver exclusivo | obrigatório | obrigatório | obrigatório |
| Restore/build sem avisos/erros | obrigatório | obrigatório | obrigatório |
| Teste de fronteiras arquiteturais e portas | obrigatório | obrigatório | obrigatório |
| Migrations em banco real | obrigatório | obrigatório | obrigatório |
| Casos de uso/CRUD e paginação determinística | obrigatório | obrigatório | obrigatório |
| EF/Dapper: commit, rollback, descarte e cancelamento | obrigatório | obrigatório | obrigatório |

Recursos de teste devem ser isolados; jobs obrigatórios falham se o servidor faltar,
em vez de reportar sucesso com todos os testes ignorados. Validações específicas incluem
schemas, tipos/chaves, parâmetros, ordenação, limites e conflitos transacionais. Migrations
não são transplantadas entre providers.

## M3 — CLI

Executar os casos de `contracts/creation-cases.json` contra o parser/CLI real. Verificar
manifesto, paths resolvidos (incluindo symlinks/junctions), scripts legados, .sln/.slnx,
erros sem fallback silencioso e cancelamento. Executar geração + scaffold + migration +
CRUD em cada provider. `specialist`, `procedure` e extensões devem anunciar suporte real
à arquitetura, e reexecução não deve sobrescrever alterações sem um mecanismo explícito.

## M4/M5 — Editores e lançamento

Verificar ambos os fluxos de criação do VS Code; criação e serviços do Visual Studio em
Windows/host suportado; compatibilidade de versões; saída grande em stdout/stderr;
nomes/pastas com espaços; capacidade ausente; cancelamento e falhas de processo.

A publicação usa o mesmo artefato validado. Atualização de template não altera código
existente, não aplica migrations e não arquiva os repositórios antigos. Documentar versões
mínimas, breaking changes e limitações antes da promoção de release.
