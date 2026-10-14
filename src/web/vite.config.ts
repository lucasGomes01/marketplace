/// <reference types="vitest/config" />
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    // Todas as chamadas de API passam pelo gateway YARP.
    proxy: {
      '/api': 'http://localhost:5000',
    },
  },
  test: {
    environment: 'jsdom',
    // Telas inteiras com a API falsa: folga para máquinas carregadas (CI, Docker rodando).
    testTimeout: 15_000,
    setupFiles: ['./src/test/setup.ts'],
    // Origem fixa para o fetch resolver URLs relativas (/api/...) nos testes.
    environmentOptions: { jsdom: { url: 'http://localhost:5173' } },
  },
})
