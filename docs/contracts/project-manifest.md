# Manifesto .openbase.json v2

Relacionado a #9. O [JSON Schema](../../contracts/openbase.schema.json) é o contrato
sintático. As regras semânticas abaixo também são obrigatórias nos consumidores.

## Conteúdo e origem

O próprio template gera o manifesto na raiz, mesmo quando criado por `dotnet new`.
O CLI pode acrescentar o campo opcional `cliVersion` sem alterar `templateVersion`.
A versão do template é a versão do pacote que produziu os arquivos, não a versão do
SDK nem a versão atual instalada do CLI.

```json
{
  "$schema": "https://raw.githubusercontent.com/britors/OpenBaseNET/main/contracts/openbase.schema.json",
  "schemaVersion": 2,
  "template": "openbasenet",
  "templateVersion": "11.0.0-preview.1",
  "architecture": "hexagonal",
  "database": "postgres",
  "rootNamespace": "MinhaApi",
  "solution": "MinhaApi.sln",
  "projects": {
    "domain": "src/MinhaApi.Domain/MinhaApi.Domain.csproj",
    "application": "src/MinhaApi.Application/MinhaApi.Application.csproj",
    "infrastructure": "src/MinhaApi.Infrastructure/MinhaApi.Infrastructure.csproj",
    "api": "src/MinhaApi.Api/MinhaApi.Api.csproj"
  },
  "persistence": {
    "dbContext": "MinhaApi.Infrastructure.Persistence.OpenBaseDbContext",
    "migrationsPath": "src/MinhaApi.Infrastructure/Persistence/Postgres/Migrations",
    "connectionStringName": "Default"
  }
}
```

Existem [exemplos completos](../../contracts/examples) para os três bancos. `schemaVersion`
v2 identifica este contrato; propriedades inesperadas são rejeitadas. Alterações incompatíveis,
incluindo novos campos que consumidores antigos não aceitem, exigem versão nova do schema.
O esquema é incluído/cacheado na ferramenta; descobrir um projeto não depende da rede.
`$schema` é uma identificação para editores, não uma ordem para executar ou baixar código.

## Regras semânticas

- Todos os caminhos são relativos ao diretório do manifesto, usam `/`, não contêm
  segmentos `.`/`..`, caminho absoluto, drive, UNC, barras invertidas ou caracteres de controle.
- Resolver caminhos (incluindo links simbólicos/junctions) não pode sair da raiz da solução.
- Os quatro projetos são distintos, existem e representam as responsabilidades informadas.
  Caminhos com espaços são válidos. A descoberta não depende do nome literal `MinhaApi`.
- `solution` aponta para `.sln` ou `.slnx` existente e inclui os quatro projetos.
- `dbContext` é o nome qualificado do tipo no projeto infrastructure. `migrationsPath`
  fica dentro desse projeto; pode não existir antes da primeira migration.
- `database` é canônico e coerente com o adaptador/driver efetivo do projeto. Não inferir
  provider procurando texto em referências condicionais não avaliadas.
- `rootNamespace` e o contexto são nomes C# válidos; validadores de entrada rejeitam
  palavras reservadas não escapadas. A regex do schema cobre apenas a forma lexical.
- `connectionStringName` é um nome de chave, não uma connection string. Não há credenciais,
  senhas ou chaves de licença no manifesto.

Validade do JSON não prova existência de arquivos, compatibilidade de assembly ou acesso
ao banco. `project info` verifica arquivos/layout; a verificação de DbContext/driver pode
exigir avaliação/build e deve ser reportada separadamente, sem abrir conexão com o banco.

## Descoberta e precedência

1. Se informado, usar `--project <diretório-ou-manifesto>` como contexto explícito.
2. Caso contrário, procurar `.openbase.json` no diretório atual e ancestrais até a raiz.
   O manifesto mais próximo define o projeto; não buscar recursivamente outros projetos
   filhos e não selecionar a primeira solução arbitrariamente.
3. Se há schemaVersion 2, validar schema e regras semânticas. Manifesto inválido retorna
   `MANIFEST_INVALID`; versão numérica desconhecida retorna `MANIFEST_VERSION_UNSUPPORTED`.
   Em ambos os casos, não contornar o erro com heurística legada.
4. Sem manifesto, ou com o formato histórico exato sem schemaVersion, executar descoberta
   legada somente de leitura. Aceitar `.sln` e `.slnx`; exigir candidato inequívoco.
5. Ambiguidade retorna `PROJECT_AMBIGUOUS`; ausência retorna `PROJECT_NOT_FOUND`.
   A resposta orienta usar contexto explícito. Nenhuma etapa reescreve a solução.

## Compatibilidade legada

O manifesto histórico contém `createdBy`, `template` (por exemplo `api:pgsql`), `version`
e `createdAt`. Nesse formato, `version` é a versão do CLI, não do template. Sua leitura
produz um descritor normalizado em memória, com `source=legacy`, `architecture=legacy`,
provider canônico e informações desconhecidas representadas por null. Não inventar versão
de template. O formato histórico e os testes de exemplo ficam em `contracts/fixtures/legacy`.

Para reconhecer sem manifesto, procurar os projetos antigos de forma inequívoca e avaliar
as referências efetivas do provider. Reconhecer explicitamente as chaves históricas
`OpenBasePostgres`, `OpenBaseSQLServer` e `OpenBaseOracle` (configuração .NET é insensível
a caixa); nunca usar simplesmente a primeira connection string encontrada.

Um descritor legado seleciona os geradores legados. A arquitetura hexagonal só pode ser
selecionada por um manifesto compatível ou por uma migração de layout explícita futura.
Chaves como `version` e `database` não são renomeadas silenciosamente no arquivo existente.

## Uma fonte de configuração por responsabilidade

Manifesto: estrutura e provider escolhido na geração. Configuration .NET: valores de
conexão por ambiente. Nenhuma segunda chave `Database:Provider` é necessária para mudar
o engine em runtime. Editar o manifesto para apontar outro banco causa inconsistência;
um comando de migração de dados/layout é fora do escopo deste primeiro contrato.
