# Protocolo do CLI para IDEs v1

Relacionado a #9 e #14. O formato é definido em M1; os comandos são implementados em M3.
O [schema de resposta](../../contracts/cli-response.schema.json) e os exemplos versionados
permitem testes de contrato compartilhados entre CLI, VS Code e Visual Studio.

## Entrada e execução

As IDEs executam `openbase` com lista de argumentos, diretório explícito e cancelamento.
Não montam comandos com shell nem interpretam mensagens traduzidas como estado. `--json`
implica não interativo: um argumento ausente retorna erro, nunca aguarda stdin. As IDEs
não passam credenciais em argumentos registrados em logs.

Os comandos definidos são:

| CLI | `command` na resposta | Contexto |
| --- | --- | --- |
| `openbase capabilities --json` | `capabilities` | Não depende de projeto |
| `openbase project info --json` | `project.info` | Aceita `--project` |
| `openbase new ... --json` | `new` | Nome, banco e pasta de saída |
| `openbase migrations list --json` | `migrations.list` | Aceita `--project` |
| `openbase migrations add <nome> --json` | `migrations.add` | Gera arquivos, não aplica |
| `openbase migrations apply --json` | `migrations.apply` | Aplica migrations; confirmação explícita |
| `openbase migrations revert <alvo> --json` | `migrations.revert` | Alvo obrigatório; confirmação explícita |
| `openbase migrations script --json` | `migrations.script` | Gera script, não altera banco |

Os detalhes de confirmação de apply/revert são concluídos na #14: a IDE deve obter
confirmação explícita do usuário e transmitir `--yes`; sem ela, modo não interativo falha
com `CONFIRMATION_REQUIRED`, código 2. `new` não aplica migrations automaticamente.
Ações potencialmente demoradas não dependem de timeout global fixo de 60 segundos.

## Envelope

Toda execução em JSON produz um único documento no stdout ao terminar:

```json
{
  "protocolVersion": 1,
  "command": "new",
  "ok": true,
  "data": { "projectRoot": "/work/MinhaApi", "manifestPath": "/work/MinhaApi/.openbase.json" },
  "error": null,
  "warnings": []
}
```

Progresso/logs ficam no stderr, sem ANSI no modo JSON. Campos de erro/aviso contêm códigos
estáveis e mensagens humanas; consumidores tomam decisões pelo código, não pela mensagem.
Caminhos resolvidos em respostas podem ser absolutos, diferentemente do manifesto em disco.
Falha exige `ok=false`, `data=null` e `error={code,message}`; sucesso exige `data` objeto e
`error=null`. Não misturar banner, saída bruta do dotnet ou segredos no stdout.

| Exit code | Significado |
| --- | --- |
| 0 | Operação concluída |
| 2 | Argumentos, validação de manifesto, contexto ambíguo ou confirmação ausente |
| 3 | Pré-requisito, pacote, versão ou capacidade indisponível |
| 4 | Falha na execução de geração, build, comando de banco ou operação solicitada |
| 130 | Cancelamento |

Em cancelamento cooperativo, emitir erro `CANCELLED` se ainda for possível escrever.
Encerramento forçado/crash pode impedir JSON; a IDE trata saída ausente/inválida como
falha de processo, nunca como sucesso. Saída de filho com código não zero não pode ser
convertida em sucesso. Não expor senha nos detalhes repassados de processos filhos.

## Negociação de capacidades

`capabilities` informa versão do CLI, versões de protocolo/manifesto suportadas,
arquiteturas, engines e comandos realmente implementados. Capacidades opcionais de
extensões são específicas por arquitetura. A lista não é uma cópia do roadmap.

A IDE verifica protocolo e comando antes de executar. Para CLI antigo sem esse comando,
classifica o cliente como legado e mantém apenas fluxos comprovadamente compatíveis,
ou orienta atualização; não presume suporte à arquitetura nova.

Comandos como `query`, `connection list` e `er`, hoje esperados por alguns serviços do
Visual Studio, não se tornam disponíveis por esta especificação. Enquanto não forem
implementados/anunciados, a interface deve exibir a indisponibilidade correspondente.

## Descoberta normalizada

`project.info` retorna `source` (manifest/legacy), arquitetura, provider canônico,
namespace, versão de template (null se desconhecida), projetos e caminhos resolvidos,
contexto/migrations e estado da verificação de provider. Não exige conexão ao servidor.
Não retorna credenciais. IDEs usam essa resposta para localizar arquivos e comandos.

Os exemplos [manifesto v2](../../contracts/examples/project-info.json) e
[layout legado](../../contracts/examples/project-info-legacy.json) fixam os nomes dos
campos de `data`. `database` é o campo do provider. Caminhos retornados são absolutos.
`schemaVersion`, `manifestPath`, `template`, `templateVersion` e `cliVersion` aceitam null
quando essa informação não existe ou não foi comprovada. O `cliVersion` desse descritor
é o registrado no projeto; a versão do executável atual vem de `capabilities`.

`projects` identifica as quatro responsabilidades, não necessariamente todos os projetos
de uma solução legada. `persistence.contextProject` identifica o projeto que contém o
DbContext, pois no layout antigo ele é separado do repositório. No layout novo, corresponde
a `projects.infrastructure`. Informação de persistência não identificada de forma inequívoca
é null; operações que a exigem falham com `PROJECT_METADATA_INCOMPLETE` (2).

`providerVerification` vale `notChecked` ou `verified`. A presença do provider no manifesto
não prova que o driver correspondente esteja referenciado. Uma divergência comprovada
retorna `PROVIDER_MISMATCH` (2), sem descritor de sucesso. Comandos que usam o provider
devem verificá-lo antes de executar. Esses exemplos ilustram respostas futuras, não resultados
obtidos de um CLI já implementado.

O formato de extensões opcionais e dos resultados detalhados de migrations é fechado
em #14 mantendo este envelope e adicionando schemas específicos por comando. Isso não
altera o contrato de criação nem o manifesto. M1 não declara esses comandos implementados.
