import fs from "node:fs";
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

const certificatePath = "certs/dev.pem";
const privateKeyPath = "certs/dev-key.pem";
const https = fs.existsSync(certificatePath) && fs.existsSync(privateKeyPath)
  ? {
      cert: fs.readFileSync(certificatePath),
      key: fs.readFileSync(privateKeyPath),
    }
  : undefined;

export default defineConfig({
  plugins: [react()],

  server: {
    port: 5173,
    host: "0.0.0.0",

    https,

    proxy: {
      "/api": "http://localhost:3000",
      "/health": "http://localhost:3000",
    },
  },

  build: {
    target: "es2020",
  },
});
