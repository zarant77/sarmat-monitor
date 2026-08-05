import { readFile } from "node:fs/promises";
import { SHARED_THRESHOLDS } from "./thresholds.js";

const DEFAULT_SERVER = Object.freeze({
  host: "0.0.0.0",
  port: 8080,
  snapshotIntervalMs: 1000,
  staleAfterMs: 5000,
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

function requireArray(value, path) {
  if (!Array.isArray(value) || value.length === 0) {
    throw new Error(`${path} must be a non-empty array`);
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

  requireArray(input.admins, "admins");
  const admins = input.admins.map((secret, index) => {
    requireNonEmptyString(secret, `admins[${index}]`);
    return secret.trim();
  });

  requireArray(input.stations, "stations");
  const stations = input.stations.map((station, index) => {
    if (!station || typeof station !== "object" || Array.isArray(station)) {
      throw new Error(`stations[${index}] must be an object`);
    }
    requireNonEmptyString(station.title, `stations[${index}].title`);
    requireNonEmptyString(station.color, `stations[${index}].color`);
    requireNonEmptyString(station.secret, `stations[${index}].secret`);
    if (station.title.trim().length > 100) {
      throw new Error(`stations[${index}].title must not exceed 100 characters`);
    }
    if (!/^#[0-9a-f]{6}$/i.test(station.color.trim())) {
      throw new Error(`stations[${index}].color must use the #RRGGBB format`);
    }
    return {
      title: station.title.trim(),
      color: station.color.trim().toUpperCase(),
      secret: station.secret.trim(),
    };
  });
  if (new Set(stations.map((station) => station.secret)).size !== stations.length) {
    throw new Error("station secrets must be unique");
  }

  return { server, admins, stations, thresholds: SHARED_THRESHOLDS };
}

export async function loadConfig(path) {
  const contents = await readFile(path, "utf8");
  return parseConfig(contents, path);
}

export function parseConfig(contents, source = "configuration") {
  let parsed;
  try {
    parsed = JSON.parse(contents);
  } catch (error) {
    throw new Error(`cannot parse config '${source}': ${error.message}`, { cause: error });
  }
  return validateConfig(parsed);
}
