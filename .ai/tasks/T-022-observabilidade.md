# T-022 — Observabilidade: OpenTelemetry + Grafana, com comparação de testes de carga

- **Serviço:** todos + Infra
- **Depende de:** T-021
- **Status:** done

## Objetivo
Ver o que acontece dentro do sistema (traces, métricas, logs) e comparar, no mesmo painel, o antes/depois de cada
técnica de escala usando os testes de carga.

## Critérios de aceite
- [x] Serviço `observability` no compose (`grafana/otel-lgtm`: coletor OTel + Prometheus + Tempo + Loki + Grafana),
      com volume (histórico sobrevive a reinícios) e 30 dias de métricas. Grafana em http://localhost:3000.
- [x] .NET (`BuildingBlocks.Observability`): traces/métricas/logs OTLP, HTTP recebido e feito, SQL Server, runtime.
      Só exporta com `OTEL_EXPORTER_OTLP_ENDPOINT` (testes e `dotnet run` não exportam nada).
- [x] Go (`internal/telemetry`): o mesmo, com `otelhttp` e logs via `slog` (stdout + OTLP).
- [x] **Trace atravessa o Kafka**: `traceparent` (W3C) nos headers — `KafkaTracing` (.NET) e `kotel` (Go).
      Verificado: um checkout vira um trace só gateway → Cart → Orders → Catalog → Orders.
- [x] Métricas de negócio: `marketplace.orders{status}`, `marketplace.order.settle.duration`,
      `marketplace.stock.reservations{outcome}` — com testes.
- [x] k6 envia para o Prometheus com etiquetas `versao` e `cenario`; início/fim viram anotações no Grafana.
- [x] Painel provisionado "Marketplace": RED por serviço, por cópia, CPU (.NET), pedidos, reservas, comparação de
      versões do k6 e traces lentos. Gerado por `observability/build-dashboard.mjs`.

## Notas
- Bug pego pelo teste novo do Go: `conflicting Schema URL` ao juntar o recurso do SDK com outra versão de convenções.
- Aviso NU1903 (alta) em `System.Security.Cryptography.Pkcs` (via EF Core → SqlClient), já presente no main:
  fixado 10.0.13 nas três `*.Infrastructure` (10.0.12 também era vulnerável).
- O Docker Desktop tem ~5 GB: construir as 5 imagens em paralelo derrubou o engine. Construir uma a uma resolve.
- Primeiro trace já mostrou que a maior parte do tempo de um pedido (~2 s) é entrega/espera no Kafka; cada serviço
  processa em 10–40 ms.
- Fora: SQL do Orders (go-mssqldb não tem instrumentação OTel pronta), CPU do Orders, lag do Kafka (precisaria de
  um exporter de Kafka).
