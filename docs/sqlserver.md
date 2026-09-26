# Adaptador SQL Server

Implementação da #10. Usa os mesmos Domain/Application, casos de uso, API e contratos
HTTP do PostgreSQL. O repositório EF e a unidade de trabalho são compartilhados na
Infrastructure; configuração, SQL Dapper e migrations são específicos do provider.

## Seleção na compilação

Enquanto o pacote de template da #6 não existe, selecione o banco ao compilar o repositório:

```bash
dotnet restore OpenBaseNET.sln -p:OpenBaseDatabase=sqlserver
dotnet build OpenBaseNET.sln -c Release --no-restore -p:OpenBaseDatabase=sqlserver
dotnet test tests/OpenBaseNET.Tests.Unit -c Release --no-build -p:OpenBaseDatabase=sqlserver
python3 scripts/check-provider.py sqlserver
```

O padrão é `postgres`; valores não implementados são rejeitados. Cada provider tem seus
próprios `obj/<provider>` e `bin/<provider>`, inclusive assets de restore. Não misturar
seleções entre restore/build/test/run. `--no-build` executa o artefato já compilado.

No Linux/macOS, `export OpenBaseDatabase=sqlserver` seleciona o provider para os comandos
filhos; no PowerShell, use `$env:OpenBaseDatabase='sqlserver'`. Essa forma também funciona
com as ferramentas EF:

```bash
export OpenBaseDatabase=sqlserver
dotnet tool restore
dotnet ef database update --project src/OpenBaseNET.Infrastructure --configuration Release
dotnet run --project src/OpenBaseNET.Api --configuration Release
```

Defina `ConnectionStrings__Default` antes de executar EF/API. Para a API em desenvolvimento,
User Secrets também é suportado; a factory EF lê somente a variável de ambiente. Não há
conexão padrão nem aplicação automática de migrations na inicialização da API.

`OpenBaseDatabase` é uma opção de build do código fonte. Não permite trocar um executável
pronto de banco alterando appsettings. O futuro comando público continua sendo
`openbase new -n MinhaApi -d sqlserver`; ele ainda depende do template/CLI nas #6/#12.

## Isolamento

O csproj exclui o pacote Npgsql e os arquivos PostgreSQL ao selecionar SQL Server, e faz
o inverso para PostgreSQL. A API chama `AddPersistence`; a implementação compilada registra
apenas o adaptador escolhido. Não há switch de provider em cada consulta ou no núcleo.

`scripts/check-provider.py` verifica os assets avaliados dos quatro projetos e o deps.json
da API, incluindo dependências transitivas. O núcleo não pode restaurar pacotes externos,
e o artefato não pode depender do driver do outro banco. Isso valida a compilação isolada;
a exclusão física dos arquivos do projeto gerado continua na #6.

## Modelo e diferenças de SQL

| Objeto | SQL Server |
| --- | --- |
| Tabela | `[dbo].[customers]` |
| ID | `uniqueidentifier NOT NULL`, gerado pelo domínio, sem identity/default do banco |
| Nome | `nvarchar(200) NOT NULL`, Unicode, validado/normalizado pelo domínio |
| Paginação | `ORDER BY name, id OFFSET @Offset ROWS FETCH NEXT @Size ROWS ONLY` |
| Índice | `(name, id)` |
| Histórico | `[dbo].[__EFMigrationsHistory]` |
| Snapshot | `Persistence/SqlServer/Migrations/SqlServerModelSnapshot.cs` |

GUIDs usam a comparação nativa de uniqueidentifier para desempatar nomes iguais. Essa
ordem pode diferir de Guid.CompareTo e da ordem do PostgreSQL; não ordenar novamente na
aplicação após paginar. Collation do banco rege a comparação dos nomes, inclusive case e
acentos. A suíte verifica um par de GUIDs cuja ordem SQL difere da ordem do .NET.

O domínio limita o nome após Trim a 200 unidades UTF-16, alinhado ao armazenamento
nvarchar(200). Não há suporte genérico a schemas fornecidos pelo usuário, chaves identity
ou procedures neste exemplo. Todos os valores Dapper são parametrizados.

As transações usam o isolamento padrão do servidor (READ COMMITTED no SQL Server padrão).
Leitores podem bloquear em escritas pendentes; não se presume o comportamento MVCC do
PostgreSQL. Não habilitamos READ_COMMITTED_SNAPSHOT, MARS ou retry de escrita automaticamente.
EF e Dapper compartilham a conexão e transação do DbContext; o escopo não suporta comandos
paralelos. A unidade de trabalho confirma uma vez e limpa o tracking ao finalizar.

Cancelamento chega ao SqlClient. Quando o driver retorna SqlException para uma operação
cujo token está cancelado, o adaptador normaliza para OperationCanceledException, preservando
o erro original como InnerException. Sem cancelamento solicitado, o erro SQL continua
sendo propagado normalmente. Falhas provocam rollback e preservam a exceção original
se a limpeza também falhar. Em perda de conexão durante commit, o resultado pode ser
desconhecido; não há repetição automática. Descartar um escopo com falha de limpeza antes
de iniciar nova operação. Detalhes comuns estão no [contrato do núcleo](core.md).

## Migrations próprias

PostgreSQL e SQL Server têm migration inicial e snapshot independentes, com nomes de
snapshot distintos. O EF procura arquivos pelo nome ao salvar o snapshot, inclusive em
pastas de providers não compilados. Reutilizar o nome padrão nos dois adaptadores pode
sobrescrever o arquivo errado. As classes `PostgresModelSnapshot` e `SqlServerModelSnapshot`
evitam essa colisão e o EF preserva seus nomes nas próximas gerações.

Ao adicionar migrations, mantenha `OpenBaseDatabase=sqlserver` e a pasta correta:

```bash
dotnet ef migrations add MinhaAlteracao --project src/OpenBaseNET.Infrastructure \
  --output-dir Persistence/SqlServer/Migrations --configuration Release
```

A CI gera uma migration de verificação em checkout descartável e verifica que o snapshot
selecionado é reutilizado e nenhum arquivo do outro provider foi alterado. Essa migration
não é aplicada nem publicada. A ferramenta não converte migrations entre engines.

## Servidores e testes

A matriz de integração usa SQL Server 2022 e 2025 Developer em Linux x64, com as imagens
[oficiais da Microsoft](https://learn.microsoft.com/en-us/sql/linux/quickstart-install-connect-docker?view=sql-server-ver17).
O build e o núcleo são testados também no Windows. Azure SQL, LocalDB e versões anteriores
do servidor não estão incluídos na matriz desta entrega.

Defina `OPENBASE_TEST_SQLSERVER` apontando para um servidor de testes com permissão de
criar/remover bancos e consultar `sys.dm_exec_requests`. A suíte cria `ob_test_<guid>` por
teste e remove apenas o banco que criou. A conexão administrativa usa master; os testes
nunca esvaziam o banco indicado pelo usuário. Nenhum teste é ignorado por falta de servidor.

```bash
dotnet test OpenBaseNET.sln --no-build --configuration Release -p:OpenBaseDatabase=sqlserver
```

Certificados de desenvolvimento podem exigir TrustServerCertificate=True na conexão de
teste; a aplicação não desabilita validação TLS globalmente. As credenciais publicadas no
workflow são exclusivas do serviço efêmero da CI.

Os testes cobrem migrations/snapshot, CRUD e paginação, Unicode/aspas/limite, commit e
rollback misturando EF/Dapper, falha SQL/SaveChanges sem retry, descarte, reaproveitamento
do escopo e cancelamento após observar espera real por lock no servidor. Os mesmos testes
HTTP e os 66 testes do núcleo rodam nas duas variantes, sem condições de banco no domínio.

EF Core.SqlServer está alinhado aos demais pacotes EF em 10.0.12. A auditoria NuGet direta
e transitiva da variante SQL Server em 2026-09-26 não reportou vulnerabilidades conhecidas.
Oracle está documentado em [oracle.md](oracle.md); o pacote NuGet unificado permanece na #6.
