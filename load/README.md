# Testes de carga (k6)

Rodam com o [k6](https://grafana.com/docs/k6/) em Docker (nada para instalar), dentro da rede do compose,
falando com o gateway como o front faz. **Só contra o ambiente local.**

Pré-requisito: `docker compose --profile apps up -d --build` no ar.

```bash
load/run.sh navegacao          # leitura: vitrine + produto, sobe até 200 usuários (~2 min)
load/run.sh compra             # carrinho → checkout → espera Confirmado, 50 compradores (~2 min)
load/run.sh compra BUYERS=150  # mais compradores
load/run.sh disputa            # 100 compradores, 10 unidades: exatamente 10 confirmados
load/run.sh estresse           # sem pausas, sobe até 2000 req/s; para sozinho quando o p95 passa de 1 s
load/run.sh lentidao           # carga contínua por 10 min para usar o site e sentir a lentidão
load/run.sh lentidao RATE=500 DURATION=5m   # mais leve / mais curto
docker stop k6-lentidao        # para qualquer cenário antes do fim (container k6-<cenário>)
```

O resumo sai no terminal e em `load/results/<cenário>.json` (ignorado pelo git).

## Cenários

| Cenário | O que prova | Limites (thresholds) |
|---|---|---|
| `navegacao` | Gateway + Catalog + SQL aguentam leitura | p95 < 500 ms, erros < 1% |
| `compra` | Escrita e o caminho assíncrono (Cart → Kafka → Orders → Catalog → Orders) | p95 < 800 ms no carrinho/checkout; `pedido_tempo_ate_confirmar` p95 < 10 s; nenhum pedido sem resposta |
| `disputa` | Concorrência no estoque | confirmados = estoque, recusados = resto, estoque final 0 |
| `lentidao` | Sentir a lentidão no navegador: 900 leituras/s + 30 compradores sem parar | nenhum (só sustenta a carga) |
| `estresse` | Onde o sistema quebra (leitura, taxa fixa crescente) | para (`abortOnFail`) com p95 > 1 s ou erros > 5% |

## Como ler

- `http_req_duration` p(95): 95% das requisições foram mais rápidas que isso.
- `http_req_failed`: taxa de erro (o 404 enquanto o pedido ainda nasce não conta).
- `pedido_tempo_ate_confirmar`: do checkout até o pedido sair de `Pending`.
  Se cresce muito, olhe o atraso (lag) dos consumer groups no Kafka UI (http://localhost:8080).
- Um ✗ num threshold faz o k6 terminar com código diferente de 0.

## Linha de base (2026-10-14, notebook de 8 núcleos, tudo no Docker Desktop)

| Cenário | Resultado |
|---|---|
| navegacao (200 usuários) | ✓ p95 67 ms, 0 erros, ~49 req/s |
| compra (50 compradores) | ✓ p95 ~280 ms no carrinho/checkout; pedido confirmado em 0,5 s (mediana), 4,6 s (p95), 1299 pedidos |
| disputa (100 × 10 unidades) | ✓ exatamente 10 confirmados, 90 recusados, estoque 0 |
| estresse | degrada perto de **~800 req/s pedidas** (p95 passou de 1 s aos ~98 s; 3250 requisições descartadas). Gargalo: **Catalog** (~1 núcleo, 97%), **gateway** (91%) e SQL Server (85%) |

**Depois da T-021** (modo Produção + cache de 5 s na vitrine, 2026-10-16): o `estresse` chegou ao fim (2000 req/s) com
p95 2,5 ms e nenhuma requisição descartada — antes, no mesmo dia, quebrava aos 1m58s. O cenário lê poucas URLs, então
quase tudo vem do cache (melhor caso).

No `compra`, o tempo até confirmar cresce sob carga (mediana 0,5 s → p95 4,6 s): fila nos consumidores Kafka.

## Cuidados

- O k6, o Docker e os serviços disputam a mesma CPU: o número vale para **esta máquina** e serve para comparar
  antes/depois de uma mudança, não para prever produção.
- Contas são criadas no `setup` (uma vez). Login a cada iteração mediria o hash de senha, que é lento de propósito.
- Cada execução cria contas e produtos novos (sufixo único); a vitrine local acumula produtos "Carga …".
