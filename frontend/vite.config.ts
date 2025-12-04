import react from "@vitejs/plugin-react";
import { defineConfig } from "vite";

export default defineConfig({
  plugins: [react()],
  // Vite automatically loads .env.production files during build
  // No need for explicit define - import.meta.env.VITE_API_URL will work
  build: {
    rollupOptions: {
      output: {
        manualChunks: {
          react: ["react", "react-dom"],
          copilot: ["@copilotkit/react-core", "@copilotkit/react-ui"],
          agui: ["@ag-ui/client"],
        },
      },
    },
    chunkSizeWarningLimit: 1600,
  },
  server: {
    host: "127.0.0.1",
    port: 5173,
    proxy: {
      "/api": {
        target: process.env.VITE_API_URL || "http://localhost:8080",
        changeOrigin: true,
        secure: false,
      },
    },
  },
});
