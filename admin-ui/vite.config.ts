import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  base: './',
  build: { outDir: '../src/CrmNlQuery.Web/wwwroot/admin-next', emptyOutDir: true, chunkSizeWarningLimit: 900 },
  server: { host: '127.0.0.1', port: 5173, proxy: { '/api': { target: 'http://127.0.0.1:5080', changeOrigin: false } } },
})
