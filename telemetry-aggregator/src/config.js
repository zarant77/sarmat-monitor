import { readFile } from "node:fs/promises";

const DEFAULT_SERVER = Object.freeze({
  host: "0.0.0.0",
  port: 8080,
  snapshotIntervalMs: 1000,
  staleAfterMs: 3000,
  offlineAfterMs: 10000,
  maxMessageBytes: 4096,
});

function requireNonEmptyString(value, path) {
  if (typeof value !== "string" || value.trim().length === 0) {
    throw new Error(`${path} must be a non-empty string`);
  }
}

function requirePositiveInteger(value, path, allowZero = false) {
  const minimum = allowZero ? 0 : 1;
  if (!Number.isInteger(value) || value < minimum) {
    throw new Error(`${path} must be an integer greater than or equal to ${minimum}`);
  }
}

export function validateConfig(input) {
  if (!input || typeof input !== "object" || Array.isArray(input)) {
    throw new Error("config must be a JSON object");
  }

  const server = { ...DEFAULT_SERVER, ...input.server };
  requireNonEmptyString(server.host, "server.host");
  requirePositiveInteger(server.port, "server.port", true);
  requirePositiveInteger(server.snapshotIntervalMs, "server.snapshotIntervalMs");
  requirePositiveInteger(server.staleAfterMs, "server.staleAfterMs");
  requirePositiveInteger(server.offlineAfterMs, "server.offlineAfterMs");
  requirePositiveInteger(server.maxMessageBytes, "server.maxMessageBytes");
  if (server.offlineAfterMs <= server.staleAfterMs) {
    throw new Error("server.offlineAfterMs must be greater than server.staleAfterMs");
  }

  if (!Array.isArray(input.stations) || input.stations.length === 0) {
    throw new Error("stations must be a non-empty array");
  }
  if (!Array.isArray(input.clients)) {
    throw new Error("clients must be an array");
  }

  const allSecrets = new Set();
  const stations = input.stations.map((station, index) => {
    requireNonEmptyString(station?.secret, `stations[${index}].secret`);
    requireNonEmptyString(station?.name, `stations[${index}].name`);
    if (typeof station?.color !== "string" || !/^#[0-9A-Fa-f]{6}$/.test(station.color)) {
      throw new Error(`stations[${index}].color must use the #RRGGBB format`);
    }
    if (allSecrets.has(station.secret)) {
      throw new Error(`duplicate secret in stations[${index}]`);
    }
    allSecrets.add(station.secret);
    return { secret: station.secret, name: station.name, color: station.color.toUpperCase() };
  });

  const clients = input.clients.map((client, index) => {
    requireNonEmptyString(client?.secret, `clients[${index}].secret`);
    requireNonEmptyString(client?.name, `clients[${index}].name`);
    if (allSecrets.has(client.secret)) {
      throw new Error(`duplicate secret in clients[${index}]`);
    }
    allSecrets.add(client.secret);
    return { secret: client.secret, name: client.name };
  });

  return { server, stations, clients };
}

export async function loadConfig(path) {
  const contents = await readFile(path, "utf8");
  let parsed;
  try {
    parsed = JSON.parse(contents);
  } catch (error) {
    throw new Error(`cannot parse config '${path}': ${error.message}`, { cause: error });
  }
  return validateConfig(parsed);
}
