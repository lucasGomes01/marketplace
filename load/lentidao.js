// Cenário 5 — lentidão contínua: mantém o sistema sob carga por um tempo para alguém usar o site e sentir.
// Diferente do `estresse`, não para sozinho. Parar antes: docker stop k6-lentidao
//   RATE      leituras por segundo (padrão 900, um pouco acima do ponto de quebra medido)
//   BUYERS    compradores comprando sem parar em paralelo (padrão 30): deixa carrinho e confirmação lentos
//   DURATION  quanto tempo (padrão 10m)
import { check } from 'k6'
import exec from 'k6/execution'
import { createAccount, createProduct, createSeller, get, post, RUN } from './lib.js'

const RATE = Number(__ENV.RATE || 900)
const BUYERS = Number(__ENV.BUYERS || 30)
const DURATION = __ENV.DURATION || '10m'

export const options = {
  setupTimeout: '5m',
  scenarios: {
    leitura: {
      executor: 'constant-arrival-rate',
      rate: RATE,
      timeUnit: '1s',
      duration: DURATION,
      preAllocatedVUs: 300,
      maxVUs: 3000,
      exec: 'browse',
    },
    compra: { executor: 'constant-vus', vus: BUYERS, duration: DURATION, exec: 'buy' },
  },
  // Sem thresholds: aqui o objetivo é sustentar a carga, não passar/falhar.
}

export function setup() {
  const seller = createSeller()
  const products = []
  for (let i = 0; i < 20; i++) {
    products.push(createProduct(seller, `Carga lentidão ${RUN} #${i}`, 1_000_000).id)
  }
  const buyers = []
  for (let i = 0; i < BUYERS; i++) {
    buyers.push(createAccount('lentidao', i))
  }
  return { products, buyers }
}

export function browse({ products }) {
  const response =
    Math.random() < 0.5
      ? get(`/api/products?page=1&pageSize=12`, null, { name: 'GET /api/products' })
      : get(`/api/products/${products[Math.floor(Math.random() * products.length)]}`, null, {
          name: 'GET /api/products/{id}',
        })
  check(response, { 'leitura 200': (r) => r.status === 200 })
}

export function buy({ products, buyers }) {
  const token = buyers[(exec.vu.idInTest - 1) % buyers.length]
  const productId = products[Math.floor(Math.random() * products.length)]
  const add = post('/api/cart/items', { productId, quantity: 1 }, token, { name: 'POST /api/cart/items' })
  if (add.status === 200) {
    const checkout = post('/api/cart/checkout', undefined, token, { name: 'POST /api/cart/checkout' })
    check(checkout, { 'checkout 202': (r) => r.status === 202 })
  }
}
