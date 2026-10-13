# .ai — Como a IA trabalha neste repositório

Esta pasta é a "memória" do projeto para agentes de IA (Claude Code, Copilot etc.).
Ela descreve **o quê** construir, **como** construir e **em que ordem**.

## Mapa

| Arquivo | Para quê |
|---|---|
| `context/product.md` | Visão, personas, escopo do MVP, glossário |
| `context/architecture.md` | Serviços, portas, comunicação, bancos |
| `context/conventions.md` | Padrões de código, testes, commits, branches |
| `context/events.md` | Tópicos e contratos dos eventos no Kafka (fonte da verdade) |
| `tasks/BACKLOG.md` | Lista ordenada de tarefas e status |
| `tasks/T-XXX-*.md` | Especificação detalhada de cada tarefa |
| `tasks/_template.md` | Modelo para novas tarefas |
| `decisions/ADR-*.md` | Decisões de arquitetura e seus porquês |
| `PROGRESS.md` | Diário curto de passagem de bastão entre sessões |

## Protocolo de trabalho (siga sempre)

1. **Ler** `PROGRESS.md` e `tasks/BACKLOG.md`. Pegar a **primeira** tarefa com status `todo`
   cujas dependências estejam `done`. Nunca pular a ordem sem o humano pedir.
2. Se a tarefa não tem arquivo `T-XXX-*.md`, **criar a partir de `_template.md`** e
   pedir revisão do humano antes de codar (é a etapa de planejamento).
3. Marcar a tarefa como `doing` no BACKLOG e criar a branch `feat/T-XXX-slug`.
4. **TDD estrito**, um critério de aceite por vez:
   - 🔴 escrever o teste que falha → rodar e ver falhar pelo motivo certo;
   - 🟢 menor código que faz passar;
   - 🔵 refatorar com os testes verdes.
5. Rodar a suíte inteira do serviço afetado (e `dotnet build` da solution) antes de concluir.
   No front, rodar **`npm test`, `npm run lint` e `npm run build`**: o Vitest não checa tipos, só o `tsc` do build.
6. Atualizar: checkboxes da tarefa, status `done` no BACKLOG, `PROGRESS.md` (3–5 linhas),
   e `context/*` se algo mudou (ex.: novo evento → `events.md`).
7. Commit em Conventional Commits referenciando a tarefa (`feat(catalog): ... [T-005]`).
   Uma tarefa = um PR.

## Regras

- **Escopo fechado:** faça só o que a tarefa pede. Ideias extras viram nova tarefa no BACKLOG
  com status `idea`.
- **Dúvida de regra de negócio → perguntar**, não inventar. Registre a resposta na tarefa.
- Nenhum serviço acessa o banco de outro. Integração só por HTTP (via contrato) ou eventos.
- Não adicionar dependências novas sem justificar na tarefa (e ADR se for estrutural).
- Nunca commitar segredos; configs locais ficam em `.env` (ver `.env.example`).
