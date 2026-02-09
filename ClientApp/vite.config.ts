import path from "path"
import react from "@vitejs/plugin-react"
import { defineConfig } from "vite"

export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: {
      "@": path.resolve(__dirname, "./src"),
    },
  },
  server: {
    port: 5173,
    proxy: {
      '/Authentication': {
        target: 'http://localhost:5277',
        secure: false,
        changeOrigin: true
      },
      '/DataManagement': {
        target: 'http://localhost:5277',
        secure: false,
        changeOrigin: true
      }
    }
  }
})