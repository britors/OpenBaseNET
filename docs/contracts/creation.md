# Contrato de criação v1

Relacionado a #8, #6 e #12. Os comandos abaixo são o contrato a implementar em M2/M3.

## Comando público

```bash
openbase new -n MinhaApi -d postgres
openbase new --name MinhaApi --database sqlserver
openbase new -n MinhaApi -d oracle --non-interactive
```

| Opção | Significado |
| --- | --- |
| `-n`, `--name` | Nome da solução e namespace raiz; obrigatório |
| `-d`, `--database` | Engine: postgres, sqlserver ou oracle |
| `-o`, `--output` | Pasta de destino; padrão: subpasta com o nome informado |
| `--non-interactive` | Proíbe prompts; argumentos ausentes causam erro |
| `--json` | Resposta JSON v1 e nenhuma pergunta; implica não interativo |
| `-s`, `--template` | Alias legado da escolha do banco; aviso de descontinuação |
| `-t`, `--type` | Compatibilidade: somente api; outros valores são rejeitados |

`-d` pertence ao OpenBase CLI. No .NET SDK, `dotnet new -d` significa `--diagnostics`.
A tradução é feita com argumentos separados, nunca concatenando uma linha de shell:

```text
dotnet new openbasenet --name MinhaApi --output MinhaApi --database postgres
```

Referência: [dotnet new](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-new).
O pacote único é `w3ti.OpenBaseNET.Template`; o shortName é `openbasenet`.
O uso direto exige `--database` e não depende do CLI. Não definimos alias curto `-d`
no host do template .NET.

## Valores, ausência e conflitos

O CLI aceita valores sem distinguir maiúsculas/minúsculas. `pgsql` e `postgresql`
são normalizados para `postgres`. `sqlserver` e `oracle` são os outros valores
canônicos. `sql`, `mssql`, valores numéricos e engines não listadas são rejeitados.
O template direto aceita apenas os valores canônicos documentados.

Sem banco, o CLI pergunta somente se houver terminal interativo, sem `--json` ou
`--non-interactive`. Sem essas condições, retorna `INPUT_REQUIRED` e exit code 2.
Não há um provider padrão silencioso. O nome continua obrigatório em todos os modos.

Se opções de banco forem repetidas, seus valores normalizados precisam concordar.
Exemplos:

- `-d postgres -s pgsql`: válido; aviso sobre `-s`.
- `--database oracle --template sqlserver`: `ARGUMENT_CONFLICT`, código 2, nenhum arquivo criado.
- `-d pgsql --database postgres`: válido.
- `-d oracle --database postgres`: erro; não adotar a última opção silenciosamente.

O nome deve ser um identificador C# ou sequência de identificadores separados por
pontos, sem palavras reservadas não escapadas. Não se aceita sintaxe `@` no nome do
projeto. A pasta `--output` pode conter espaços e é tratada como um único argumento.
Diretório inexistente pode ser criado; diretório vazio pode ser usado. Destino não vazio
retorna `DESTINATION_NOT_EMPTY` (2) sem sobrescrever. O primeiro contrato não oferece
`--force` para contornar essa proteção.

## Configuração do banco

Escolher o engine não exige servidor, credenciais, conexão ou importação de tabelas.
`--db-name` continua sendo o nome do banco/serviço, não o tipo de engine. A base usa
uma chave de conexão estável `ConnectionStrings:Default`, vazia no template.

Os parâmetros legados `--db-server`, `--db-name`, `--db-user`, `--db-password` são
reconhecidos na transição, mas o novo fluxo não os grava em arquivos versionados.
Se fornecidos, a configuração de desenvolvimento usa User Secrets no projeto Api.
Senhas não aparecem em logs, manifesto nem respostas JSON. A ajuda recomenda User
Secrets ou variáveis de ambiente para evitar segredos na linha de comando.

Não é feito teste de conexão nem importação de tabelas automaticamente em `new`.
Essas operações pertencem a comandos separados. `--mediatr-license` e
`--automapper-license` são aceitos temporariamente e ignorados no layout novo,
com aviso sem exibir o valor; o gerador legado mantém seu comportamento aplicável.

## Efeitos e erros

1. Validar opções, SDK, versão/capacidades do template e destino antes de escrever.
2. Gerar a solução com apenas o provider escolhido e manifesto v2.
3. Validar manifesto e referências de caminhos.
4. Aplicar configuração de desenvolvimento somente quando solicitada.
5. Retornar os caminhos criados; build, migrations e deploy são operações separadas.

O CLI não instala/atualiza silenciosamente ferramentas globais durante `new`. Se o
pacote compatível não estiver instalado, retorna `TEMPLATE_NOT_INSTALLED` (3) e
orienta executar `openbase install`. Atualização do pacote não modifica projetos existentes.

Cancelamento usa exit code 130. Se uma falha ocorrer depois da geração, a saída informa
a pasta e o estágio concluído; não apaga arquivos que o usuário possa ter alterado.
Não anuncia sucesso completo em configuração parcial.

## Compatibilidade

Os aliases antigos são mantidos durante a primeira linha major do CLI com suporte a
este contrato. A retirada exige anúncio de breaking change em uma major posterior.
Os três pacotes/shortNames antigos continuam identificáveis nas operações explícitas
de versão/histórico; não são desinstalados ou convertidos automaticamente.

A confirmação de que todos os geradores entendem o manifesto/arquitetura é requisito
para anunciar suporte ao template unificado. Aceitar `-d` isoladamente não basta.
