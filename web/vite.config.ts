import react from '@vitejs/plugin-react'
import { defineConfig } from 'vitest/config'

// In development the API runs on :5090; proxying keeps the browser on one origin (no CORS, cookies-ready).
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': 'http://localhost:5090',
      '/hubs': { target: 'http://localhost:5090', ws: true },
    },
  },
  test: {
    environment: 'jsdom',
  },
})
