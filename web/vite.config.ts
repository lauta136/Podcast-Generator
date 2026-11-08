/// <reference types="vitest/config" />
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

export default defineConfig({
  plugins: [react()],
  server: {
    // The API runs on its own port; keys and providers never reach the browser.
    proxy: { '/api': 'http://localhost:5273' },
  },
  test: {
    environment: 'node',
  },
})
