import { resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { createTelemetryServer } from "./app.js";
import { loadConfig, parseConfig, validateConfig } from "./config.js";

const projectDirectory = resolve(fileURLToPath(new URL("..", import.meta.url)));
const configPath = resolve(process.env.SARMAT_CONFIG ?? resolve(projectDirectory, "config.json"));

try {
  let config = process.env.SARMAT_CONFIG_JSON
    ? parseConfig(process.env.SARMAT_CONFIG_JSON, "SARMAT_CONFIG_JSON")
    : await loadConfig(configPath);

  if (process.env.PORT) {
    config = validateConfig({
      ...config,
      server: { ...config.server, port: Number(process.env.PORT) },
    });
  }

  const server = createTelemetryServer(config);
  const address = await server.listen();
  console.info(`Telemetry aggregator listening on ws://${address.address}:${address.port}`);

  let stopping = false;
  async function stop(signal) {
    if (stopping) return;
    stopping = true;
    console.info(`Received ${signal}; shutting down`);
    await server.close();
  }

  process.on("SIGINT", () => void stop("SIGINT"));
  process.on("SIGTERM", () => void stop("SIGTERM"));
} catch (error) {
  console.error(error.message);
  process.exitCode = 1;
}
