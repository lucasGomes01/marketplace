// Cenário 2 — compra completa: adicionar ao carrinho → finalizar → esperar o pedido ser confirmado.
// Mede também o tempo do caminho assíncrono (Cart → Kafka → Orders → Catalog → Orders).
import { check, sleep } from 'k6'
import { Counter, Trend } from 'k6/metrics'
import exec from 'k6/execution'
import { createAccount, createProduct, createSeller, post, RUN, waitForOrder } from './lib.js'

const BUYERS = Number(__ENV.BUYERS || 50)

const timeToConfirm = new Trend('pedido_tempo_ate_confirmar', true)
const confirmed = new Counter('pedidos_confirmados')
const notSettled = new Counter('pedidos_sem_resposta')

export const options = {
  setupTimeout: '5m',
  scenarios: {
    compra: {
      executor: 'ramping-vus',
      stages: [
        { duration: '30s', target: Math.ceil(BUYERS / 2) },
        { duration: '1m', target: BUYERS },
        { duration: '20s', target: 0 },
      ],
    },
  },
  thresholds: {
    http_req_failed: ['rate<0.01'],
    'http_req_duration{name:POST /api/cart/items}': ['p(95)<800'],
    'http_req_duration{name:POST /api/cart/checkout}': ['p(95)<800'],
    pedido_tempo_ate_confirmar: ['p(95)<10000'],
    pedidos_sem_resposta: ['count==0'],
  },
}

export function setup() {
  // Contas criadas uma vez: o login (hash de senha) não entra na medição.
  const seller = createSeller()
  const products = []
  for (let i = 0; i < 10; i++) {
    products.push(createProduct(seller, `Carga compra ${RUN} #${i}`, 1_000_000).id)
  }
  const buyers = []
  for (let i = 0; i < BUYERS; i++) {
    buyers.push(createAccount('comprador', i))
  }
  return { products, buyers }
}

export default function ({ products, buyers }) {
  // Cada usuário virtual usa sempre a mesma conta (cada um tem o próprio carrinho).
  const token = buyers[(exec.vu.idInTest - 1) % buyers.length]
  const productId = products[Math.floor(Math.random() * products.length)]

  const add = post('/api/cart/items', { productId, quantity: 1 }, token, { name: 'POST /api/cart/items' })
  if (!check(add, { 'adicionou ao carrinho': (r) => r.status === 200 })) {
    return
  }

  const checkout = post('/api/cart/checkout', undefined, token, { name: 'POST /api/cart/checkout' })
  if (!check(checkout, { 'checkout 202': (r) => r.status === 202 })) {
    return
  }

  const result = waitForOrder(checkout.json().orderId, token)
  if (result === null) {
    notSettled.add(1)
    return
  }
  check(result, { 'pedido confirmado': (r) => r.status === 'Confirmed' })
  if (result.status === 'Confirmed') {
    confirmed.add(1)
    timeToConfirm.add(result.ms)
  }

  sleep(1)
}
