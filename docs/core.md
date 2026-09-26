# Núcleo Customer

Implementação da issue #2. `src/OpenBaseNET.Domain` e `src/OpenBaseNET.Application`
compilam com .NET 10 e não têm pacotes externos. O mesmo código será incluído nas três
variantes; não há seleção de banco nesses projetos. Infrastructure/PostgreSQL e Api já
completam os quatro projetos de produção; veja a [integração PostgreSQL](postgres.md).

## Domínio e casos de uso

`Customer` gera seu ID na criação e protege o nome: obrigatório, normalizado com `Trim`
e limitado a 200 unidades UTF-16 após normalização. O ID não pode ser alterado. `Rename`
valida antes de modificar a entidade; um nome inválido preserva o estado anterior.
O construtor privado permite materialização pelo EF com mapeamento explícito posterior.

As classes de Application são as portas de entrada. Cada uma tem `ExecuteAsync` e recebe
suas portas de saída no construtor, sem dispatcher ou interface de repasse:

| Caso de uso | Entrada | Saída | Portas usadas |
| --- | --- | --- | --- |
| CreateCustomer | name, cancellationToken | CustomerResponse | ICustomerRepository, IUnitOfWork |
| GetCustomer | id, cancellationToken | CustomerResponse | ICustomerQueries |
| ListCustomers | pageNumber, pageSize, cancellationToken | IReadOnlyList de CustomerResponse | ICustomerQueries |
| UpdateCustomer | id, name, cancellationToken | CustomerResponse | ICustomerRepository, IUnitOfWork |
| DeleteCustomer | id, cancellationToken | Task | ICustomerRepository, IUnitOfWork |

Atualização e remoção estabelecem o padrão para o scaffold da #13. Repetir uma remoção
de entidade ausente retorna not found. DTOs são projeções; alterar um DTO não altera uma
entidade rastreada. `CustomerResponse` contém apenas ID e nome.

## Portas e transações

`ICustomerQueries` retorna projeções, sem expor tracking, SQL ou tipos do banco.
`ICustomerRepository` atende escritas: Add/Remove são síncronos e apenas registram mudanças;
GetAsync carrega uma entidade rastreada dentro da unidade de trabalho. Não há SaveChanges
nessa porta nem confirmação por operação de repositório.

`IUnitOfWork.ExecuteAsync<T>` executa uma callback uma única vez, salva alterações e retorna
somente após o commit. Create/Update validam nomes e IDs antes de chamar essa porta;
Delete valida o ID. IDs não vazios mas inexistentes exigem consulta e produzem erro próprio.
Reads não abrem transação de escrita. Falhas das portas propagam sem retry automático.

O adaptador deve compartilhar conexão/transação entre EF e Dapper, rejeitar transações
aninhadas e, em falha, fazer rollback e descartar estado de tracking incompatível. Falhas
de limpeza não substituem a exceção original. Um erro de transporte durante commit pode
deixar o resultado desconhecido no servidor; o núcleo não repete a operação automaticamente.

CancellationToken acompanha toda fronteira assíncrona. Cancelamento anterior à chamada
não acessa portas; cancelamento observado após uma consulta não vira sucesso ou not found.
O adaptador também deve respeitar o token em conexão, comandos e commit e usar uma política
própria de limpeza que funcione mesmo após cancelamento da requisição.

## Paginação

`CustomerPage` valida página >= 1, tamanho entre 1 e 100 (padrão 20) e offset dentro de
Int32. O cálculo intermediário usa Int64 para rejeitar overflow antes de consultar o banco.

A porta exige **Name ASC, Id ASC antes de offset/limit**. O ID único desempata nomes
iguais. A aplicação preserva a ordem retornada; ordenar após limitar o resultado quebraria
a paginação. Os adaptadores precisam testar esse contrato com nomes repetidos em banco real.
Collation de texto e comparação de UUID são responsabilidade do provider: a ordem é estável
para um mesmo conjunto de dados no mesmo banco, sem prometer equivalência de collation
entre engines. Escritas concorrentes podem deslocar páginas; não há snapshot entre chamadas.
Uma página após o fim retorna coleção vazia, sem contagem total implícita.

## Contrato HTTP

O núcleo não conhece códigos HTTP. Api faz o mapeamento abaixo, validado por testes HTTP:

| Resultado | Resposta HTTP |
| --- | --- |
| CreateCustomer concluído | 201 com CustomerResponse e Location para GET por ID |
| GetCustomer/UpdateCustomer concluído | 200 com CustomerResponse |
| ListCustomers concluído | 200 com array, inclusive vazio |
| DeleteCustomer concluído | 204 |
| CustomerNotFoundException | 404 ProblemDetails, code CUSTOMER_NOT_FOUND |
| DomainValidationException/InputValidationException | 422 ProblemDetails, code e field da exceção |
| JSON inválido ou falha de model binding | 400 |
| Cancelamento da requisição | Propagar/encerrar a requisição, sem registrar ou responder como 500 |
| Falha inesperada | 500 sanitizado; detalhes técnicos apenas no log |

Os códigos de validação são CUSTOMER_ID_REQUIRED, CUSTOMER_NAME_REQUIRED,
CUSTOMER_NAME_TOO_LONG, PAGE_NUMBER_OUT_OF_RANGE e PAGE_SIZE_OUT_OF_RANGE.
Consumidores não devem inferir códigos a partir do texto das mensagens.

## Validação e limites da entrega

```bash
dotnet restore OpenBaseNET.sln
dotnet build OpenBaseNET.sln --no-restore --configuration Release
dotnet test tests/OpenBaseNET.Tests.Unit --no-build --configuration Release
```

Os testes usam doubles que registram consultas, alterações, entrada na unidade de trabalho
e falhas simuladas. Eles verificam invariantes, rejeição antes das portas, limites de página,
propagação de tokens/erros, espera pelo commit e dependências dos assemblies/assinaturas das
portas. Não usam banco, HTTP, Moq, MediatR ou AutoMapper. A CI executa no Linux e no Windows.

Esses doubles **não provam rollback, durabilidade, ordenação SQL ou compartilhamento de
transação EF/Dapper**. A suíte de integração PostgreSQL da #3 verifica esses cenários em
banco real; SQL Server/Oracle continuam nas #10/#11. A comparação do núcleo entre aplicações
efetivamente geradas depende da #6. Não há template NuGet unificado ou comandos novos de CLI publicados.
