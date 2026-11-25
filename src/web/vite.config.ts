import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

export default defineConfig({
  plugins: [react()],
  server: {
    port: Number(process.env.WEB_PORT ?? 5173),
    // In dev the API runs separately (dotnet run → :5080); in production the API serves the built SPA.
    proxy: { '/api': process.env.API_URL ?? 'http://localhost:5080' },
  },
})
