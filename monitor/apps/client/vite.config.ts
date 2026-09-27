import fs from "node:fs";
import { fileURLToPath } from "node:url";
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

const monitorPackagePath = fileURLToPath(new URL("../../package.json", import.meta.url));
const { version } = JSON.parse(fs.readFileSync(monitorPackagePath, "utf8")) as { version: string };
const buildTimestamp = new Date();
const buildDate = [
  String(buildTimestamp.getDate()).padStart(2, "0"),
  String(buildTimestamp.getMonth() + 1).padStart(2, "0"),
  buildTimestamp.getFullYear(),
].join(".");

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

  define: {
    __APP_VERSION__: JSON.stringify(version),
    __BUILD_DATE__: JSON.stringify(buildDate),
  },

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
