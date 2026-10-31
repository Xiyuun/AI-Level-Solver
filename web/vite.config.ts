import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// 开发代理：前端 5173 → 平台 5000（dotnet run --urls http://localhost:5000）
export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      '/api': 'http://localhost:5000',
    },
  },
})
