# ADR-002 — TDD com Testcontainers

- **Data:** 2026-09-29
- **Status:** aceita

## Decisão
- Desenvolvimento orientado a testes (red → green → refactor) em todas as tarefas.
- Testes de integração usam **dependências reais** (SQL Server, Kafka) via Testcontainers,
  em vez de banco em memória ou mocks de DbContext.

## Porquê
- O provider InMemory/SQLite do EF se comporta diferente do SQL Server (constraints, transações, tipos).
- Testes com a infra real dão confiança para a IA refatorar sem quebrar nada.

## Consequências
- É preciso ter Docker rodando para executar os testes de integração (localmente e no CI).
- Os testes ficam mais lentos. Mitigado com um container por classe/coleção e a maioria dos testes no domínio.
