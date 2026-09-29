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
})
