# T-017 — Trocar RabbitMQ por Kafka

- **Serviço:** Infra + docs
- **Depende de:** T-013
- **Status:** done

## Objetivo
Usar Kafka como barramento de eventos (ver `decisions/ADR-003-kafka.md`) antes de existir código de mensageria.

## Critérios de aceite
- [x] `docker compose up -d` sobe Kafka (KRaft) saudável, cria os 4 tópicos (3 partições) e sobe o Kafka UI em :8080.
- [x] RabbitMQ removido do compose, do `.env.example`, das configs dos serviços e do `BuildingBlocks.Messaging`.
- [x] Serviços recebem `Kafka__BootstrapServers` (.NET) / `ORDERS_KAFKA_BROKERS` (Go).
- [x] É possível produzir e consumir uma mensagem num tópico pelo compose (teste manual com as ferramentas do Kafka).
- [x] Docs atualizadas: `events.md` (tópicos, chaves, grupos), `architecture.md`, `conventions.md`, `CLAUDE.md`,
      `README.md`, ADR-001 (aponta para o ADR-003) e as tarefas T-010..T-012 do BACKLOG.
- [x] `docker compose --profile apps up` continua subindo tudo e os health checks respondem pelo gateway.

## Fora de escopo
- Publicar/consumir eventos de verdade nos serviços (T-010 em diante).

## Notas
- 2026-10-13: Kafka 4.1 KRaft + `kafka-init` (4 tópicos, 3 partições) + Kafka UI (:8080). Testado: produzir/consumir com chave, tópico não declarado é recusado (`UNKNOWN_TOPIC_OR_PARTITION`), os 4 serviços sobem e respondem via gateway.
- `cart.checked_out` virou **`cart.checked-out`**: o Kafka avisa que misturar `.` e `_` colide nas métricas.
- Listeners: `kafka:19092` (dentro do compose) e `localhost:9092` (sua máquina, testes e ferramentas).
- No Git Bash, comandos `docker compose exec` com caminho absoluto precisam de `MSYS_NO_PATHCONV=1`.
