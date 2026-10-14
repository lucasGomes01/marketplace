// Cenário 1 — navegação (só leitura): vitrine paginada e página de produto, subindo até 200 usuários.
import { check, sleep } from 'k6'
import { createProduct, createSeller, get, RUN } from './lib.js'

export const options = {
  setupTimeout: '2m',
  stages: [
    { duration: '20s', target: 10 },
    { duration: '30s', target: 50 },
    { duration: '30s', target: 100 },
    { duration: '30s', target: 200 },
    { duration: '20s', target: 0 },
  ],
  thresholds: {
    http_req_failed: ['rate<0.01'],
    'http_req_duration{name:GET /api/products}': ['p(95)<500'],
    'http_req_duration{name:GET /api/products/{id}}': ['p(95)<500'],
  },
}

export function setup() {
  // Alguns produtos garantidos para as páginas de detalhe.
  const seller = createSeller()
  const ids = []
  for (let i = 0; i < 20; i++) {
    ids.push(createProduct(seller, `Carga navegação ${RUN} #${i}`, 100).id)
  }
  return { ids }
}

export default function ({ ids }) {
  const page = 1 + Math.floor(Math.random() * 3)
  const list = get(`/api/products?page=${page}&pageSize=12`, null, { name: 'GET /api/products' })
  check(list, { 'vitrine 200': (r) => r.status === 200 })

  sleep(Math.random() * 2) // pessoa olhando a vitrine

  const id = ids[Math.floor(Math.random() * ids.length)]
  const product = get(`/api/products/${id}`, null, { name: 'GET /api/products/{id}' })
  check(product, { 'produto 200': (r) => r.status === 200 })

  sleep(1 + Math.random() * 2)
}
