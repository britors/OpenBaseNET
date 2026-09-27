# Template único

Implementação da #6. O pacote `w3ti.OpenBaseNET.Template` oferece o shortName
`openbasenet`, com `--database postgres|sqlserver|oracle` obrigatório. Ele gera quatro
projetos de produção, dois projetos de testes e `.openbase.json` v2.

## Empacotar e instalar localmente

Requer Python 3 e SDK .NET 10 estável. A versão informada é gravada no pacote e no
manifesto gerado; não é obtida da versão do SDK ou do CLI.

```bash
python3 scripts/pack-template.py --version 11.0.0-preview.1
dotnet new install artifacts/packages/w3ti.OpenBaseNET.Template.11.0.0-preview.1.nupkg
dotnet new openbasenet --name MinhaApi --database postgres
dotnet new openbasenet --name Acme.Customers --database sqlserver --output "Acme Customers"
dotnet new openbasenet --name MinhaApiOracle --database oracle
```

No Windows, use `python` no lugar de `python3`. O pacote ainda não está publicado no
NuGet. A instalação acima é explícita e local; o suporte do OpenBase CLI continua na #12.
No `dotnet new`, `-d` pertence ao SDK (diagnóstico), não é alias de `--database`.
O template direto aceita os três valores documentados; `pgsql` é um alias futuro do CLI.

Gerar não restaura pacotes nem abre conexão com um banco. Depois da criação, siga o
README da aplicação para restore/build, User Secrets, migrations e testes reais.
As proteções e validações específicas de `openbase new`, incluindo destino não vazio,
pertencem ao CLI; o uso direto segue as opções e o comportamento do SDK `dotnet new`.

## Conteúdo da aplicação

- Domain e Application vêm dos mesmos fontes para os três bancos, sem dependências NuGet.
- Infrastructure recebe somente os arquivos de configuração, DI, consultas e migrations
  do provider escolhido. As referências ao driver e suas versões são incondicionais.
- Api compartilha os endpoints Customer e recebe um identificador de User Secrets novo
  em cada geração. `ConnectionStrings:Default` permanece vazia.
- Os testes unitários e a suíte HTTP são compartilhados; os testes de persistência são
  somente os do banco escolhido. Falta de servidor nos testes de integração causa falha.
- Solução, projetos, namespaces, metadados EF e caminhos do manifesto usam o nome solicitado.
  O DbContext continua denominado `OpenBaseDbContext` dentro do namespace da aplicação.

Não há propriedade `OpenBaseDatabase` nas aplicações geradas: a decisão foi tomada na
geração. Editar `.openbase.json` ou atualizar o pacote de template não converte um banco
nem modifica o código de aplicações existentes.

## Fonte do pacote

`scripts/pack-template.py` monta uma pasta temporária a partir de uma lista explícita de
fontes e arquivos de configuração. Domain/Application não têm cópias mantidas à parte.
As referências condicionais usadas para desenvolver os três adaptadores no repositório
são convertidas em condições do mecanismo de templates; os arquivos gerados incluem
somente o provider escolhido. A seleção de pastas está em `packaging/template.json`.

A pasta temporária contém o projeto de empacotamento e os conteúdos; `dotnet pack` cria
o `.nupkg`. Não entram bin/obj, `.git`, workflows, resultados de testes ou configurações
locais adicionais. O template não executa scripts nem post-actions na máquina de quem gera.

## Validação

```bash
python3 -m venv .venv
.venv/bin/python -m pip install -r requirements-contracts.txt
.venv/bin/python scripts/check-template.py --package artifacts/packages/w3ti.OpenBaseNET.Template.11.0.0-preview.1.nupkg
```

No Windows, use `.venv\Scripts\python.exe`. A validação instala o pacote em uma hive
isolada, rejeita banco ausente/inválido sem produzir arquivos e gera `MinhaApi` e
`Acme.Customers` para cada banco, em pastas com espaços. Valida schema/caminhos/versão do
manifesto, exclusão de arquivos e drivers, identidade do núcleo, identificadores de
User Secrets, restore, build e os 66 testes unitários de cada solução.

Com os servidores e variáveis de teste configurados, acrescente `--database oracle
--integration` (ou outro banco). Essa opção também executa a suíte real de persistência/HTTP
e gera uma migration de verificação, reutilizando o snapshot renomeado. `--output <pasta-nova>`
preserva aplicações e relatórios TRX; sem essa opção, o diretório temporário é removido.

O workflow Template valida o mesmo pacote em Linux/Windows, com os três bancos.
Os workflows de PostgreSQL 16/18, SQL Server 2022/2025 e Oracle Free 23.26.3 também
empacotam, geram e executam aplicações contra seus servidores reais. A #5 continua
responsável por CLI/IDEs e pelos gates da futura publicação; esta etapa não publica NuGet.
