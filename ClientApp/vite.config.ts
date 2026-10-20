import path from "path"
import react from "@vitejs/plugin-react"
import { defineConfig } from "vite"

const backendTarget = "http://localhost:5277"

export default defineConfig({
  plugins: [react()],

  resolve: {
    alias: {
      "@": path.resolve(__dirname, "./src"),
    },
  },

  server: {
    host: "0.0.0.0",
    port: 5173,
    strictPort: true,

    // Permite qualquer hostname do Cloudflare Quick Tunnel.
    allowedHosts: [
      ".trycloudflare.com",
    ],

    /*
     * Não hardcodar hostname do Quick Tunnel aqui.
     * O endereço pode mudar quando o túnel for recriado.
     */
    hmr: {
      protocol: "wss",
      clientPort: 443,
    },

    proxy: {
      "/api": {
        target: backendTarget,
        changeOrigin: true,
        secure: false,
      },

      "/Authentication": {
        target: backendTarget,
        changeOrigin: true,
        secure: false,
      },

      "/DataManagement": {
        target: backendTarget,
        changeOrigin: true,
        secure: false,
      },
    },
  },
})
