import react from '@vitejs/plugin-react'
import { defineConfig } from 'vitest/config'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      // The dev server serves the UI on http://localhost:5173 and forwards API
      // calls to the API. The API runs phone-testing mode: HTTP on 5132, bound
      // for the LAN so the staff mobile app can reach it too.
      '/api': {
        target: 'http://localhost:5132',
        changeOrigin: true,
      },
    },
  },
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
  },
})
