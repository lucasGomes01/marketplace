#!/usr/bin/env bash
# Roda um cenário de carga com o k6 em Docker, dentro da rede do compose (fala direto com o gateway).
# Uso: load/run.sh navegacao|compra|disputa|estresse|lentidao [VAR=valor ...]   ex.: load/run.sh compra BUYERS=100
#
# Com o Grafana no ar (serviço observability), os resultados vão para o Prometheus com as etiquetas
# `versao` e `cenario`, e o início/fim do teste vira uma marca nos gráficos. Para comparar experimentos, nomeie:
#   VERSAO=3-catalogs load/run.sh estresse      (padrão: commit atual, ex. "a1b2c3d")
set -euo pipefail
cd "$(dirname "$0")"
scenario="${1:?cenário: navegacao | compra | disputa | estresse | lentidao}"; shift
envs=(); for kv in "$@"; do envs+=(-e "$kv"); done
versao="${VERSAO:-$(git rev-parse --short HEAD 2>/dev/null || echo local)}"
grafana="${GRAFANA_URL:-http://localhost:3000}"
mkdir -p results

out=(); out_env=()   # opção do k6 / variáveis do container
if curl -fs -o /dev/null "$grafana/api/health"; then
  out=(-o experimental-prometheus-rw)
  out_env=(-e K6_PROMETHEUS_RW_SERVER_URL=http://observability:9090/api/v1/write
    -e "K6_PROMETHEUS_RW_TREND_STATS=p(95),p(99),avg,max"
    -e K6_PROMETHEUS_RW_PUSH_INTERVAL=5s)
fi

annotate() { # texto -> marca no Grafana (ignora falha: o teste não depende disso)
  [ ${#out[@]} -gt 0 ] || return 0
  curl -fs -o /dev/null -X POST "$grafana/api/annotations" -H 'Content-Type: application/json' \
    -d "{\"tags\":[\"k6\",\"$scenario\",\"$versao\"],\"text\":\"k6 $scenario ($versao) $1\"}" || true
}

annotate "início"
status=0
MSYS_NO_PATHCONV=1 docker run --rm -i --name "k6-$scenario" --network marketplace_default \
  -v "$(pwd -W 2>/dev/null || pwd):/scripts" -w /scripts "${envs[@]}" "${out_env[@]}" \
  grafana/k6:2.3.0 run "${out[@]}" --summary-export "results/$scenario.json" \
  --tag versao="$versao" --tag cenario="$scenario" "$scenario.js" || status=$?
annotate "fim"
exit $status
