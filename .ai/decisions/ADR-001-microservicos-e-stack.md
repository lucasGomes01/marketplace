# ADR-001 — Microsserviços com .NET + Go

- **Data:** 2026-09-29
- **Status:** aceita

## Contexto
O projeto é também um laboratório de aprendizado e portfólio. Os objetivos incluem praticar comunicação
entre serviços, mensageria, poliglotismo e testes de integração realistas.

## Decisão
- Serviços Users, Catalog e Cart em **.NET 10 + EF Core + SQL Server**.
- Orders em **Go**, para exercitar um segundo ecossistema e provar que os contratos (HTTP e eventos) são
  independentes de linguagem.
- **YARP** como API Gateway (mesmo ecossistema .NET, configuração por JSON).
- **RabbitMQ** para eventos; **um banco por serviço** na mesma instância de SQL Server (isolamento lógico).

## Consequências
- (+) Fronteiras claras, contratos explícitos em `context/events.md`.
- (−) Mais complexidade operacional que um monólito; consistência eventual no checkout.
- (−) Dois toolchains no CI. Mitigado com jobs separados.
- Se a complexidade atrapalhar o avanço, considerar consolidar Users/Catalog/Cart num monólito modular.
