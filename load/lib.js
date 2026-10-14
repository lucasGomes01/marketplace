// Funções comuns aos cenários de carga. Tudo passa pelo gateway, como o front.
import http from 'k6/http'
import { check, fail, sleep } from 'k6'

export const BASE_URL = __ENV.BASE_URL || 'http://gateway:8080'
export const PASSWORD = 'Senha-forte-123'
// Sufixo único por execução: os cenários rodam num banco que já tem dados.
export const RUN = `${Date.now()}`

const json = (token) => ({
  headers: { 'Content-Type': 'application/json', ...(token ? { Authorization: `Bearer ${token}` } : {}) },
})

export function post(path, body, token, tags = {}) {
  return http.post(`${BASE_URL}${path}`, body === undefined ? null : JSON.stringify(body), { ...json(token), tags })
}

export function get(path, token, tags = {}) {
  return http.get(`${BASE_URL}${path}`, { ...json(token), tags })
}

function ok(response, what) {
  if (response.status < 200 || response.status >= 300) {
    fail(`${what}: HTTP ${response.status} ${response.body}`)
  }
  return response.json()
}

/** Cria a conta e devolve o token. O hash de senha é lento de propósito: use só na preparação (setup). */
export function createAccount(prefix, index) {
  const email = `${prefix}-${RUN}-${index}@load.test`
  ok(post('/api/users', { email, name: `${prefix} ${index}`, password: PASSWORD }), `cadastro ${email}`)
  return ok(post('/api/auth/login', { email, password: PASSWORD }), `login ${email}`).accessToken
}

/** Conta vendedora pronta para anunciar (o token novo já tem o papel Seller). */
export function createSeller() {
  const token = createAccount('vendedor', 0)
  return ok(post('/api/users/me/seller', undefined, token), 'ativar vendedor').accessToken
}

export function createProduct(sellerToken, name, stock, price = 99.9) {
  return ok(post('/api/products', { name, price, stock }, sellerToken), `produto ${name}`)
}

/** Espera o pedido sair de Pending (ou nascer). Devolve { status, ms } ou null se estourar o prazo. */
export function waitForOrder(orderId, token, timeoutSeconds = 60) {
  const start = Date.now()
  while (Date.now() - start < timeoutSeconds * 1000) {
    // 404 é esperado aqui: o pedido ainda não nasceu (o checkout é assíncrono).
    const response = http.get(`${BASE_URL}/api/orders/${orderId}`, {
      ...json(token),
      tags: { name: 'GET /api/orders/{id}' },
      responseCallback: http.expectedStatuses(200, 404),
    })
    if (response.status === 200) {
      const { status } = response.json()
      if (status !== 'Pending') {
        return { status, ms: Date.now() - start }
      }
    }
    sleep(0.5)
  }
  return null
}

export { check }
