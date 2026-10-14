# T-020 — Testes de carga com k6

- **Serviço:** Infra
- **Depende de:** T-016
- **Status:** done

## Objetivo
Saber quanto o sistema aguenta nesta máquina, onde ele engasga primeiro, e provar que a concorrência no estoque
continua correta sob carga.

## Critérios de aceite
- [x] k6 em Docker (`grafana/k6:2.3.0`), na rede do compose, sem instalar nada: `load/run.sh <cenário>`.
- [x] `navegacao`: até 200 usuários lendo vitrine/produto; p95 < 500 ms, erros < 1%.
- [x] `compra`: carrinho → checkout → espera o pedido; mede o tempo até confirmar; nenhum pedido sem resposta.
- [x] `disputa`: 100 compradores × 10 unidades → exatamente 10 confirmados, 90 recusados, estoque 0.
- [x] `estresse`: taxa crescente sem pausas, para sozinho quando o p95 passa de 1 s → ponto de quebra.
- [x] Linha de base registrada em `load/README.md`.

## Notas
- Contas criadas no `setup`: login por iteração mediria o hash de senha (lento de propósito).
- O 404 ao consultar um pedido que ainda não nasceu é marcado como esperado (`expectedStatuses(200, 404)`).
- Resultado: tudo verde nos 3 cenários realistas; estresse degrada perto de ~800 req/s, gargalo Catalog (CPU),
  depois gateway e SQL Server. Sob compra, o tempo até confirmar cresce (fila nos consumidores) → ideia I-12 e
  réplicas/partições.
