# OpenBaseNET

Base unificada para templates .NET com arquitetura hexagonal e adaptadores PostgreSQL,
SQL Server e Oracle. O desenvolvimento é acompanhado no [plano de execução #7](https://github.com/britors/OpenBaseNET/issues/7).

Os contratos do M1 estão definidos. Os quatro projetos de produção já implementam Customer
com API HTTP e adaptadores PostgreSQL/SQL Server/Oracle, selecionados na compilação, com migrations
próprias e testes de integração. O template unificado pode ser empacotado e instalado
localmente; os novos comandos do OpenBase CLI continuam em M3. Não há pacote unificado
publicado no NuGet.

## Gerar uma aplicação

```bash
python3 scripts/pack-template.py --version 11.0.0-preview.1
dotnet new install artifacts/packages/w3ti.OpenBaseNET.Template.11.0.0-preview.1.nupkg
dotnet new openbasenet --name MinhaApi --database postgres
```

Use `sqlserver` ou `oracle` para os outros bancos. No Windows, use `python`.
A escolha é obrigatória; gerar não exige conexão com o banco nem OpenBase CLI.
Veja [empacotamento, conteúdo e validação do template](docs/template.md).

## Experiência definida

```bash
openbase new -n MinhaApi -d postgres
openbase new -n MinhaApi -d sqlserver
openbase new -n MinhaApi -d oracle
```

O OpenBase CLI traduz a escolha para `dotnet new openbasenet --name MinhaApi --database postgres`.
No `dotnet new`, `-d` continua significando diagnóstico. A aplicação gerada terá quatro
projetos de produção e somente os arquivos e dependências do banco escolhido.

## Contratos

- [Núcleo implementado, testes e contrato dos adaptadores](docs/core.md)
- [PostgreSQL: configuração, migrations, API e testes reais](docs/postgres.md)
- [SQL Server: compilação isolada, configuração e testes reais](docs/sqlserver.md)
- [Oracle: RAW/GUID, schemas, migrations e testes reais](docs/oracle.md)
- [Arquitetura e direção das dependências](docs/adr/0001-hexagonal-architecture.md)
- [Criação, opções e compatibilidade](docs/contracts/creation.md)
- [Manifesto e descoberta de projetos](docs/contracts/project-manifest.md)
- [Protocolo do CLI para os editores](docs/contracts/cli-protocol.md)
- [JSON Schema do manifesto v2](contracts/openbase.schema.json)
- [Matriz de aceitação](docs/contracts/acceptance.md)

Os exemplos em `contracts/examples` e a validação do pacote usam `11.0.0-preview.1`.
Essa versão local de desenvolvimento não anuncia uma publicação disponível no NuGet.

## Compilar e testar o núcleo

Requer SDK .NET 10 estável. `global.json` aceita feature bands posteriores dentro da
linha 10.0. As versões dos pacotes de teste estão em `Directory.Packages.props`; Domain
e Application usam somente a biblioteca padrão do .NET.

```bash
dotnet restore OpenBaseNET.sln
dotnet build OpenBaseNET.sln --no-restore --configuration Release
dotnet test tests/OpenBaseNET.Tests.Unit --no-build --configuration Release
```

A CI valida o núcleo e o build no Linux/Windows; outra matriz executa testes reais com
PostgreSQL 16/18, SQL Server 2022/2025 e Oracle Free 23.26.3. O build padrão seleciona PostgreSQL; para SQL Server,
use `-p:OpenBaseDatabase=sqlserver` em restore/build/test (ou `oracle` para Oracle). Cada variante possui seus próprios
artefatos e depende somente do driver escolhido. Para a suíte completa, configure a conexão
de testes conforme a documentação do adaptador.

## Validar os contratos

```bash
python3 -m venv .venv
.venv/bin/python -m pip install -r requirements-contracts.txt
.venv/bin/python -m unittest discover -s tests/contracts -v
```

No Windows, use `.venv\Scripts\python.exe`. Essa validação cobre schemas, exemplos,
casos inválidos e consistência dos contratos; não substitui build nem testes de integração
com .NET e os bancos, executados pelas suítes próprias e pela validação das aplicações geradas.

## Repositórios relacionados

A implementação do template fica aqui. O CLI e as interfaces continuam em
[OpenBase.CLI](https://github.com/britors/OpenBase.CLI),
[OpenBase.Vscode](https://github.com/britors/OpenBase.Vscode) e
[OpenBase.VisualStudio](https://github.com/britors/OpenBase.VisualStudio).
Os templates anteriores continuam disponíveis durante a transição.

Licença do código: [MIT](LICENSE.txt).
