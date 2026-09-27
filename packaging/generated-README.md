# OpenBaseTemplate

API .NET 10 com arquitetura hexagonal e banco **__OPENBASE_DATABASE__**.
A solução contém quatro projetos de produção e dois de testes. Domain/Application são
independentes de banco; Infrastructure contém somente o adaptador escolhido na geração.

## Compilar e testar

```bash
dotnet restore OpenBaseTemplate.sln
dotnet build OpenBaseTemplate.sln --no-restore --configuration Release
dotnet test tests/OpenBaseTemplate.Tests.Unit --no-build --configuration Release
```

Criar a aplicação e executar os testes unitários não exige servidor de banco.

## Configurar e executar

A chave `ConnectionStrings:Default` começa vazia. Configure a conexão de desenvolvimento
com User Secrets ou com a variável `ConnectionStrings__Default`, sem gravar senhas no Git:

```bash
dotnet user-secrets set "ConnectionStrings:Default" "<conexão de desenvolvimento>" --project src/OpenBaseTemplate.Api
dotnet run --project src/OpenBaseTemplate.Api
```

Cada aplicação gerada recebe um identificador próprio de User Secrets. A API expõe
`/api/customers` com criação, consulta, paginação, atualização e remoção.
Não aplica migrations automaticamente durante o startup.

## Migrations e testes de integração

Configure `ConnectionStrings__Default` antes de executar as ferramentas EF. Para os
testes reais, configure também a variável administrativa descrita na
[documentação de __OPENBASE_DATABASE__](https://github.com/britors/OpenBaseNET/blob/main/docs/__OPENBASE_DATABASE__.md).
Cada teste cria e remove seus próprios recursos; a falta de banco causa falha.

```bash
dotnet tool restore
dotnet ef database update --project src/OpenBaseTemplate.Infrastructure
dotnet ef migrations add MinhaAlteracao --project src/OpenBaseTemplate.Infrastructure --output-dir Persistence/__OPENBASE_PROVIDER_FOLDER__/Migrations
dotnet test tests/OpenBaseTemplate.Tests.Integration --configuration Release
```

O manifesto `.openbase.json` identifica o banco, os projetos e a versão do template.
Trocar o banco de uma aplicação existente exige uma migração de código e dados; editar
o manifesto não converte a aplicação. Atualizar o pacote de template também não modifica
o código de aplicações já criadas.
