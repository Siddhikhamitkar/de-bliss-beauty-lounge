import react from '@vitejs/plugin-react'
import { defineConfig } from 'vitest/config'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      // The dev server serves the UI on http://localhost:5173 and forwards API
      // calls to the API's HTTPS URL. `secure: false` accepts the ASP.NET Core
      // development certificate, which is not in Node's trust store.
      '/api': {
        target: 'https://localhost:7019',
        secure: false,
        changeOrigin: true,
      },
    },
  },
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
  },
})
