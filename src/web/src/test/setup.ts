import '@testing-library/jest-dom/vitest'
import { cleanup } from '@testing-library/react'
import { afterAll, afterEach, beforeAll } from 'vitest'
import { resetFakeCarts } from './cart'
import { resetFakeProducts } from './products'
import { resetFakeApi, server } from './server'

beforeAll(() => server.listen({ onUnhandledFrame: 'error' }))

afterEach(() => {
  cleanup()
  server.resetHandlers()
  resetFakeApi()
  resetFakeProducts()
  resetFakeCarts()
  localStorage.clear()
})

afterAll(() => server.close())
