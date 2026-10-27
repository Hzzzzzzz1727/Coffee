import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

export default defineConfig({
  plugins: [vue()],
  build: {
    outDir: 'server-dotnet/wwwroot',
    emptyOutDir: true,
  },
  server: {
    host: '0.0.0.0',
    port: 5173,
    watch: {
      ignored: ['**/server-dotnet/bin/**', '**/server-dotnet/obj/**', '**/server-dotnet/wwwroot/**'],
    },
    proxy: {
      '/api': {
        target: 'http://127.0.0.1:3032',
        changeOrigin: true,
      },
      '/uploads': {
        target: 'http://127.0.0.1:3032',
        changeOrigin: true,
      },
    },
  },
})
