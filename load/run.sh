#!/usr/bin/env bash
# Roda um cenário de carga com o k6 em Docker, dentro da rede do compose (fala direto com o gateway).
# Uso: load/run.sh navegacao|compra|disputa [VAR=valor ...]   ex.: load/run.sh compra BUYERS=100
set -euo pipefail
cd "$(dirname "$0")"
scenario="${1:?cenário: navegacao | compra | disputa}"; shift
envs=(); for kv in "$@"; do envs+=(-e "$kv"); done
mkdir -p results
MSYS_NO_PATHCONV=1 docker run --rm -i --name "k6-$scenario" --network marketplace_default \
  -v "$(pwd -W 2>/dev/null || pwd):/scripts" -w /scripts "${envs[@]}" \
  grafana/k6:2.3.0 run --summary-export "results/$scenario.json" "$scenario.js"
