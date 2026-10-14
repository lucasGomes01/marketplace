import { defineConfig, devices } from '@playwright/test'

/**
 * E2E contra o sistema de verdade: backend no `docker compose --profile apps` (gateway em :5000) e o front
 * compilado servido pelo `vite preview` (que repassa /api para o gateway, como o dev server).
 */
const port = 4173

export default defineConfig({
  testDir: './tests',
  // O fluxo é longo (conta, produto, carrinho, pedido assíncrono via Kafka).
  timeout: 120_000,
  expect: { timeout: 10_000 },
  fullyParallel: false,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI ? [['list'], ['html', { open: 'never' }]] : 'list',
  use: {
    baseURL: `http://localhost:${port}`,
    locale: 'pt-BR',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  webServer: {
    command: `npm run build && npm run preview -- --port ${port} --strictPort`,
    cwd: '../src/web',
    url: `http://localhost:${port}`,
    reuseExistingServer: !process.env.CI,
    timeout: 120_000,
  },
})
