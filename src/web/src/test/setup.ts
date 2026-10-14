import '@testing-library/jest-dom/vitest'
import { cleanup, configure } from '@testing-library/react'
import { afterAll, afterEach, beforeAll } from 'vitest'
import { orderPolling } from '../api/orders'
import { resetFakeCarts } from './cart'
import { resetFakeOrders } from './orders'
import { resetFakeProducts } from './products'
import { resetFakeApi, server } from './server'

// findBy*/waitFor esperam até 3 s (o padrão de 1 s falhava com a máquina carregada).
configure({ asyncUtilTimeout: 3000 })

// Consultas do status do pedido rápidas nos testes (o padrão é 1,5 s).
const testPolling = { intervalMs: 5, maxAttempts: 8 }

beforeAll(() => {
  server.listen({ onUnhandledFrame: 'error' })
  Object.assign(orderPolling, testPolling)
})

afterEach(() => {
  cleanup()
  server.resetHandlers()
  resetFakeApi()
  resetFakeProducts()
  resetFakeCarts()
  resetFakeOrders()
  Object.assign(orderPolling, testPolling)
  localStorage.clear()
})

afterAll(() => server.close())
