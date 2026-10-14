// Cenário 3 — disputa de estoque: 100 compradores finalizam ao mesmo tempo um produto com 10 unidades.
// Correto: exatamente 10 pedidos confirmados, 90 recusados e estoque 0 (nunca negativo, nunca 11 confirmados).
import { check } from 'k6'
import { Counter } from 'k6/metrics'
import exec from 'k6/execution'
import { createAccount, createProduct, createSeller, get, post, RUN, waitForOrder } from './lib.js'

const BUYERS = Number(__ENV.BUYERS || 100)
const STOCK = Number(__ENV.STOCK || 10)

const confirmed = new Counter('disputa_confirmados')
const rejected = new Counter('disputa_recusados')
const notSettled = new Counter('disputa_sem_resposta')

export const options = {
  setupTimeout: '5m',
  scenarios: {
    // Todos ao mesmo tempo: cada usuário virtual finaliza uma vez.
    disputa: { executor: 'per-vu-iterations', vus: BUYERS, iterations: 1, maxDuration: '3m' },
  },
  thresholds: {
    disputa_confirmados: [`count==${STOCK}`],
    disputa_recusados: [`count==${BUYERS - STOCK}`],
    disputa_sem_resposta: ['count==0'],
    checks: ['rate==1'],
  },
}

export function setup() {
  const seller = createSeller()
  const product = createProduct(seller, `Carga disputa ${RUN}`, STOCK)
  const buyers = []
  for (let i = 0; i < BUYERS; i++) {
    const token = createAccount('disputa', i)
    // Todos colocam no carrinho antes (ainda há estoque); a disputa é no checkout.
    const add = post('/api/cart/items', { productId: product.id, quantity: 1 }, token)
    if (add.status !== 200) {
      throw new Error(`carrinho ${i}: HTTP ${add.status} ${add.body}`)
    }
    buyers.push(token)
  }
  return { productId: product.id, buyers }
}

export default function ({ buyers }) {
  const token = buyers[exec.vu.idInTest - 1]
  const checkout = post('/api/cart/checkout', undefined, token, { name: 'POST /api/cart/checkout' })
  if (!check(checkout, { 'checkout 202': (r) => r.status === 202 })) {
    return
  }
  const result = waitForOrder(checkout.json().orderId, token, 120)
  if (result === null) notSettled.add(1)
  else if (result.status === 'Confirmed') confirmed.add(1)
  else rejected.add(1)
}

export function teardown({ productId }) {
  const stock = get(`/api/products/${productId}`).json().stock
  check(stock, { 'estoque final 0 (nunca negativo)': (s) => s === 0 })
}
