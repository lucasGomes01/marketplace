# ADR-003 — Kafka no lugar do RabbitMQ

- **Data:** 2026-10-13
- **Status:** aceita (substitui a parte de mensageria do ADR-001)

## Contexto
O dono do projeto pediu Kafka. Nenhum código publicava ou consumia mensagens ainda (a mensageria começava na
T-010), então a troca custa só infra e documentação.

## Decisão
- **Apache Kafka 4.1** em modo **KRaft** (sem ZooKeeper), um broker no desenvolvimento.
- **Um tópico por tipo de evento**: `cart.checked-out`, `order.placed`, `stock.reserved`, `stock.rejected`.
  3 partições, replicação 1 (local).
- **Criação automática de tópicos desligada**: os tópicos são declarados num só lugar (serviço `kafka-init` do
  compose). Tópico novo = nova linha lá + contrato em `context/events.md`.
- **Chave da mensagem = id do agregado** (`orderId`, `cartId`) para manter a ordem dos eventos de um mesmo pedido.
- **Um consumer group por serviço** (`orders-service`, `catalog-service`, `cart-service`).
- Clientes: **Confluent.Kafka** (.NET) e **franz-go** (`github.com/twmb/franz-go`, Go puro, sem cgo, compatível
  com a imagem distroless do Orders).
- **Kafka UI** (`kafbat/kafka-ui`) em `http://localhost:8080` para inspecionar tópicos e mensagens.
- Testes de integração com **Testcontainers.Kafka** (.NET) e o módulo Kafka do testcontainers-go.

## Consequências
- (+) Log durável e reprocessável: dá para "rebobinar" um consumer group e reprocessar eventos.
- (+) Ordem garantida por chave dentro da partição.
- (−) Entrega **at-least-once**: consumidores continuam obrigatoriamente idempotentes (guardar `eventId`).
- (−) Kafka não tem dead-letter queue nativa como o RabbitMQ: falhas definitivas vão para um tópico
  `<tópico>.dlq` publicado pelo próprio consumidor (decidir o formato na T-010).
- (−) Imagem e uso de memória maiores que o RabbitMQ no ambiente local.
