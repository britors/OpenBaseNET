# Adaptador PostgreSQL e API

Implementação da #3 e avanço da #4. A solução contém os quatro projetos de produção:
Domain, Application, Infrastructure e Api. O adaptador PostgreSQL está operacional;
SQL Server e Oracle também têm adaptadores isolados. PostgreSQL é a seleção de build
padrão deste repositório (`OpenBaseDatabase=postgres`); o [template](template.md) fixa
o banco na geração e exclui os arquivos dos demais providers.

## Persistência

| Objeto | Contrato |
| --- | --- |
| Tabela | `public.customers` |
| Chave | `id`, PostgreSQL `uuid`, gerado pelo domínio, sem default de banco |
| Nome | `name`, `varchar(200) NOT NULL`, normalizado/validado pelo domínio |
| Índice de paginação | `(name, id)` |
| Histórico de migrations | `public."__EFMigrationsHistory"` |
| DbContext | `OpenBaseNET.Infrastructure.Persistence.OpenBaseDbContext` |
| Connection string | `ConnectionStrings:Default` |

Não há conversão de schemas arbitrários, procedures ou chaves numéricas neste exemplo.
Caracteres Unicode e aspas são preservados; valores de consultas são parâmetros, nunca
concatenados ao SQL. Valem os limites de texto do PostgreSQL (incluindo ausência de NUL).
O domínio limita o nome a 200 unidades UTF-16; o limite do tipo no banco é de 200 caracteres.
As regras de nome não vazio/normalização pertencem ao domínio, não a uma trigger.

A consulta Dapper aplica `ORDER BY name ASC, id ASC LIMIT ... OFFSET ...` no banco.
O desempate é único. Collation vem do banco; paginação por offset não mantém snapshot entre
requisições concorrentes. As escritas EF usam rastreamento e materialização pelo construtor
privado da entidade. Não há retry automático nem detecção de edição concorrente por versão;
atualizações concorrentes seguem o comportamento do banco, sem ETag/controle otimista próprio.

## Conexão e transação

O DbContext scoped é dono da conexão Npgsql, criada fechada e aberta sob demanda. As
consultas Dapper usam `GetDbConnection()` e `CurrentTransaction.GetDbTransaction()`.
Fora de uma transação, o Dapper abre/fecha a conexão conforme necessário; não a descarta.
EF permanece responsável pelo lifetime. O escopo não permite operações paralelas.

`IUnitOfWork` abre a transação, executa a callback uma única vez, chama SaveChanges e
confirma. Add/Remove apenas registram mudanças e exigem transação ativa. Consultas dentro
da callback enxergam alterações já enviadas ao banco; mudanças EF ainda não salvas não
aparecem em consultas SQL até SaveChanges. A aplicação normalmente deixa esse flush para
o final da unidade de trabalho.

Em erro/cancelamento, a unidade faz rollback, descarta a transação e limpa o tracking.
Rollback usa um token independente da requisição cancelada. Falhas de limpeza são registradas
sem substituir a exceção original; nesse caso, descartar o escopo antes de uma nova operação.
Após commit/rollback normal, o mesmo escopo pode iniciar outra unidade de trabalho.
Transações aninhadas/concomitantes são rejeitadas. Uma transação iniciada pelo chamador não
é assumida nem desfeita pela unidade de trabalho.

Perda de conexão durante commit pode deixar resultado desconhecido; não há repetição cega
de escrita. Descartar uma transação EF ainda não confirmada provoca rollback. Não iniciar
trabalho em segundo plano que continue usando o DbContext após o término do escopo HTTP.

Referências do desenho: [transações do EF Core](https://learn.microsoft.com/en-us/ef/core/saving/transactions)
e [uso básico do Npgsql](https://www.npgsql.org/doc/basic-usage.html).

## Migrations e execução

Requer SDK .NET 10 e um PostgreSQL. A CI testa PostgreSQL 16 e 18 em Linux; o núcleo e o
build da solução também são verificados no Windows. Sem matriz dedicada, outras versões
do servidor não são anunciadas como testadas.

```bash
dotnet restore OpenBaseNET.sln
dotnet tool restore
dotnet build OpenBaseNET.sln --no-restore --configuration Release
```

Para ferramentas EF, defina `ConnectionStrings__Default` no ambiente com a conexão do
banco desejado. A factory exige essa configuração explicitamente e não escolhe banco
de destino silenciosamente. Ela não lê User Secrets da API.

```bash
dotnet ef database update --project src/OpenBaseNET.Infrastructure --configuration Release
dotnet run --project src/OpenBaseNET.Api --configuration Release
```

A API também aceita User Secrets em desenvolvimento:

```bash
dotnet user-secrets set 'ConnectionStrings:Default' '<sua conexão>' --project src/OpenBaseNET.Api
```

O arquivo appsettings mantém a conexão vazia. A API não aplica migrations na inicialização.
A conta usada para migrations precisa de permissão DDL; a conta da aplicação precisa das
permissões de leitura/escrita correspondentes. Criar uma migration não exige servidor ativo,
mas a factory ainda exige uma connection string sintaticamente válida.

O scaffold EF usa a pasta `Persistence/Postgres/Migrations` e a classe `PostgresModelSnapshot`.
Snapshot e migrations são
versionados juntos. Não reutilizar esse conjunto para outro provider.

## HTTP e logging

`/api/customers` oferece POST/GET; `/api/customers/{id}` oferece GET/PUT/DELETE. POST
retorna 201 com Location, consultas/alteração retornam 200, remoção retorna 204. Ausência
retorna 404, validação de domínio/entrada retorna 422, JSON inválido retorna 400. Erros
inesperados retornam ProblemDetails 500 sem SQL, nomes de drivers ou credenciais.
Cancelamento da requisição não é convertido em erro interno.

O host configura uma única pilha de logging padrão do .NET. Não há Serilog, MediatR,
AutoMapper ou wrappers HTTP obrigatórios; autenticação e demais extensões são opcionais
e ainda não foram incorporadas ao template unificado.

## Testes reais

Defina `OPENBASE_TEST_POSTGRES` com uma conexão a um servidor de testes com permissão
CREATE DATABASE. A suíte cria um banco `ob_test_<guid>` para cada teste, aplica migrations
e remove apenas esse banco ao terminar. Não esvazia nem remove o banco indicado na
configuração. Ausência de servidor/configuração falha o teste, sem skips silenciosos.

```bash
dotnet test OpenBaseNET.sln --no-build --configuration Release
```

Para executar apenas o núcleo, sem PostgreSQL:

```bash
dotnet test tests/OpenBaseNET.Tests.Unit --no-build --configuration Release
```

Os testes reais verificam CRUD HTTP e persistência, migrations/model snapshot, ordenação
com nomes repetidos, Unicode, tamanho máximo, isolamento antes do commit, commit/rollback
misturando EF e Dapper, falha no SaveChanges e no commit, ausência de retry, descarte e
reutilização do escopo. Cancelamento é disparado após observar uma consulta EF/Dapper
realmente bloqueada no PostgreSQL. O TestServer HTTP fica no processo; o banco é real.

## Dependências

EF Core/Relational/Design e ferramentas estão alinhados em 10.0.12; provider Npgsql 10.0.3
e Dapper 2.1.89. Relational tem referência explícita para evitar divergência entre a versão
transitiva mínima do provider e a usada pelo Design. Design é privado ao projeto de infra.
Versões ficam em Directory.Packages.props e `.config/dotnet-tools.json`.

Em 2026-09-26, `dotnet list OpenBaseNET.sln package --vulnerable --include-transitive
--no-restore --format json` não reportou vulnerabilidades conhecidas no NuGet para os seis
projetos. Domain/Application continuam sem pacotes. SQL Server e Oracle têm auditorias
e isolamento de compilação próprios. A [validação do template](template.md) verifica
também a exclusão física de arquivos e referências dos bancos não selecionados.
