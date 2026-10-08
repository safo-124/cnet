import path from 'node:path'
import tailwindcss from '@tailwindcss/vite'
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: {
      '@': path.resolve(__dirname, './src'),
    },
  },
  server: {
    // Forward API calls to the ASP.NET Core backend, so the browser sees one origin (no CORS in development).
    proxy: {
      '/api': 'http://localhost:5236',
      '/samples': 'http://localhost:5236',
    },
  },
})
