// Cenário 4 — estresse: sem pausas entre cliques, sobe a carga até o sistema ficar lento.
// O teste para sozinho quando o p95 passa de 1 s (abortOnFail): o ponto de parada é o limite desta máquina.
import { check } from 'k6'
import { createProduct, createSeller, get, RUN } from './lib.js'

export const options = {
  setupTimeout: '2m',
  scenarios: {
    estresse: {
      executor: 'ramping-arrival-rate', // requisições por segundo fixas, independente de quanto o sistema demora
      startRate: 50,
      timeUnit: '1s',
      preAllocatedVUs: 200,
      maxVUs: 2000,
      stages: [
        { duration: '30s', target: 200 },
        { duration: '30s', target: 400 },
        { duration: '30s', target: 800 },
        { duration: '30s', target: 1200 },
        { duration: '30s', target: 1600 },
        { duration: '30s', target: 2000 },
      ],
    },
  },
  thresholds: {
    http_req_duration: [{ threshold: 'p(95)<1000', abortOnFail: true, delayAbortEval: '10s' }],
    http_req_failed: [{ threshold: 'rate<0.05', abortOnFail: true, delayAbortEval: '10s' }],
  },
}

export function setup() {
  const seller = createSeller()
  const ids = []
  for (let i = 0; i < 20; i++) {
    ids.push(createProduct(seller, `Carga estresse ${RUN} #${i}`, 100).id)
  }
  return { ids }
}

export default function ({ ids }) {
  const response =
    Math.random() < 0.5
      ? get(`/api/products?page=1&pageSize=12`, null, { name: 'GET /api/products' })
      : get(`/api/products/${ids[Math.floor(Math.random() * ids.length)]}`, null, { name: 'GET /api/products/{id}' })
  check(response, { '200': (r) => r.status === 200 })
}
