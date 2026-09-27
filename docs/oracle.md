# Adaptador Oracle

Implementação da #11, com os mesmos Domain, Application, API, repositório EF e unidade
de trabalho dos outros bancos. O provider e as consultas Dapper são selecionados no build.

## Compilar e executar

```bash
export OpenBaseDatabase=oracle
dotnet restore OpenBaseNET.sln
dotnet build OpenBaseNET.sln --no-restore --configuration Release
dotnet test tests/OpenBaseNET.Tests.Unit --no-build --configuration Release
python3 scripts/check-provider.py oracle
```

No PowerShell, use `$env:OpenBaseDatabase='oracle'`. Também é possível passar
`-p:OpenBaseDatabase=oracle` em restore/build/test/run, mantendo a mesma seleção em cada
comando. Artefatos ficam em `obj/oracle` e `bin/oracle`; as dependências avaliadas não
incluem Npgsql nem SqlClient. A exclusão física dos arquivos gerados pertence à #6.

Configure `ConnectionStrings__Default` com usuário da aplicação, senha e o serviço da PDB,
por exemplo `User Id=minha_api;Password=<senha>;Data Source=localhost:1521/FREEPDB1`.
Não use SYSTEM como usuário da aplicação. O DBA deve provisionar o schema e suas cotas;
as migrations criam objetos dentro desse schema, sem criar usuários ou PDBs.

```bash
dotnet tool restore
dotnet ef database update --project src/OpenBaseNET.Infrastructure --configuration Release
dotnet run --project src/OpenBaseNET.Api --configuration Release
```

API e ferramentas EF exigem conexão explícita; não há migrations automáticas no startup.
A factory EF lê a variável de ambiente; a API também aceita User Secrets em desenvolvimento.
`OpenBaseDatabase` seleciona o código compilado, não troca o banco de um binário pronto.
O comando público planejado `openbase new -n MinhaApi -d oracle` depende das #6/#12.

## Modelo e particularidades

| Objeto | Oracle |
| --- | --- |
| Tabela | `CUSTOMERS`, no schema do usuário conectado |
| ID | `RAW(16) NOT NULL`, GUID criado no domínio |
| Nome | `NVARCHAR2(200) NOT NULL`, Unicode |
| Paginação | `ORDER BY NAME, ID OFFSET :page_offset ROWS FETCH NEXT :page_size ROWS ONLY` |
| Índice | `IX_CUSTOMERS_NAME_ID (NAME, ID)` |
| Histórico | `"__EFMigrationsHistory"`, no mesmo schema |
| Snapshot | `Persistence/Oracle/Migrations/OracleModelSnapshot.cs` |

O EF converte GUID para `Guid.ToByteArray()`; o Dapper envia RAW e reconstrói com
`new Guid(bytes)`. Esse layout não equivale à representação textual hexadecimal do GUID.
Integrações externas devem usar a mesma conversão. A ordenação do desempate é a binária
do RAW armazenado; pode diferir de Guid.CompareTo e dos outros bancos. A ordenação de
nomes segue as configurações Oracle da sessão/banco, sem reordenar depois da paginação.

As consultas Dapper configuram `BindByName=true` por comando e tipos explícitos RAW/Int32.
Não alteram configuração global do ODP.NET. Os parâmetros de paginação são registrados
em ordem diferente da ocorrência no SQL para que os testes detectem binding posicional.

Oracle trata string vazia como NULL. O domínio rejeita nomes nulos, vazios ou só com
espaços antes de acessar o banco, e aplica Trim/limite de 200 unidades UTF-16. NVARCHAR2
preserva Unicode; os testes incluem acentos, caracteres japoneses, aspas e o limite.
Identificadores da tabela/colunas são maiúsculos. Não há schema fixo, sinônimos ou troca
de CURRENT_SCHEMA por operação.

EF e Dapper usam a mesma conexão e transação. O escopo executa comandos sequencialmente,
com READ COMMITTED padrão, sem retries automáticos. Leitura comum usa consistência de
leitura do Oracle e não bloqueia da mesma forma que no SQL Server padrão. DDL pode fazer
commit implícito no Oracle: execute migrations separadamente das transações de negócio.

Cancelamento é propagado ao ODP.NET; quando o token foi cancelado e o driver retorna
OracleException ou uma OperationCanceledException com token interno, o adaptador normaliza
para OperationCanceledException com o token da chamada e preserva o erro original como
InnerException. Sem cancelamento solicitado, erros Oracle são propagados.
Falhas provocam rollback e limpeza do tracking. Perda de conexão no commit pode deixar
resultado desconhecido; não se repete uma escrita automaticamente. Se a limpeza falhar,
descarte o escopo. Consulte também o [contrato transacional](core.md).

## Migrations

```bash
dotnet ef migrations add MinhaAlteracao --project src/OpenBaseNET.Infrastructure \
  --output-dir Persistence/Oracle/Migrations --configuration Release
```

Mantenha `OpenBaseDatabase=oracle` durante a geração. Snapshot e migration inicial são
exclusivos do Oracle; o nome OracleModelSnapshot evita colisão com os snapshots dos
outros providers. A CI verifica scaffolding em checkout descartável, sem aplicar a
migration de verificação. Migrations não são convertidas entre bancos.

## Servidor e testes

A CI usa Oracle AI Database Free, imagem `gvenzl/oracle-free:23.26.3-slim-faststart`,
com o serviço FREEPDB1 em Linux x64. Essa é a versão de servidor coberta nesta entrega;
19c, 21c, Autonomous Database e outras configurações não integram a matriz. O build e os
testes do núcleo também rodam no Windows. O provider é Oracle.EntityFrameworkCore
10.23.26301, com compatibilidade SQL DatabaseVersion23 e EF Core 10.0.12.

Para executar a integração, configure `OPENBASE_TEST_ORACLE` com uma conexão administrativa
para uma PDB de testes. Ela deve poder criar/remover usuários, conceder CREATE SESSION,
CREATE TABLE, CREATE VIEW e CREATE PROCEDURE, atribuir cota no tablespace USERS e consultar
V$SESSION. Os testes criam `OB_TEST_<guid>` por caso e removem apenas esse schema gerado.
Pooling fica desabilitado nas conexões desses testes para permitir remover os usuários
depois de encerrar as sessões. Falta de servidor/configuração é falha, nunca skip.

### Cancelamento e rede do servidor

A imagem de testes configura `DISABLE_OOB=ON` e `BREAK_POLL_SKIP=1000` no `sqlnet.ora`.
Essa configuração foi introduzida para contornar um problema do proxy do Docker, conforme
o [mantenedor da imagem](https://github.com/gvenzl/oci-oracle-xe/issues/43). Ela interfere
no recebimento dos sinais de interrupção pelo servidor; alterar apenas `DisableOOB` no
cliente não habilita esse mecanismo no servidor.

A CI conecta diretamente ao IP do container e configura `DISABLE_OOB=OFF` no servidor
efêmero antes de abrir as conexões de teste. Ao reiniciar o listener, executa
`ALTER SYSTEM REGISTER` para registrar novamente o serviço `FREEPDB1`. O ODP.NET mantém
sua configuração padrão. Esse caminho evita o proxy que motivou a configuração da imagem.
Para reproduzir a suíte em outro ambiente, o servidor e o caminho de rede precisam
permitir o sinal de interrupção. Consulte a definição de
[DISABLE_OOB no Oracle Net](https://docs.oracle.com/en/database/oracle/oracle-database/26/netrf/parameters-for-the-sqlnet.ora.html).

### Executar a suíte

```bash
dotnet test OpenBaseNET.sln --no-build --configuration Release
```

A suíte cobre migrations/snapshot, contratos HTTP comuns, CRUD, paginação, RAW/GUID,
commit/rollback misturando EF/Dapper, erros SQL, falha no SaveChanges e no commit por
constraint diferida, descarte e reutilização do escopo. Para cancelamento, uma view temporária de teste chama uma função com transação
autônoma que aguarda um lock de linha mantido por outra conexão. Um observador confirma
a espera em V$SESSION antes de cancelar as consultas reais EF/Dapper; não basta um token
previamente cancelado. A conexão bloqueadora libera o lock em finally, inclusive em
falha. A view e a função existem apenas no schema isolado desse teste.

Referências: [provider oficial no NuGet](https://www.nuget.org/packages/Oracle.EntityFrameworkCore/10.23.26301),
[API do provider Oracle](https://docs.oracle.com/en/database/oracle/oracle-database/26/odpnt/EFCoreAPI.html)
e [imagem/integração com GitHub Actions](https://github.com/gvenzl/oci-oracle-free).

A auditoria NuGet direta/transitiva da variante Oracle em 2026-09-26 não reportou
vulnerabilidades conhecidas. A latência de cancelamento de PL/SQL é descrita na
[documentação de OracleCommand.Cancel](https://docs.oracle.com/en/database/oracle/oracle-database/26/odpnt/CommandCancel.html).
