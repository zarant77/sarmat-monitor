import { readFile } from "node:fs/promises";

const DEFAULT_SERVER = Object.freeze({
  host: "0.0.0.0",
  port: 8080,
  snapshotIntervalMs: 1000,
  staleAfterMs: 3000,
  offlineAfterMs: 10000,
  maxMessageBytes: 4096,
});

const DEFAULT_THRESHOLDS = Object.freeze({
  voltage: { goodMin: 44, normalMin: 42 },
  current: { goodMax: 80, normalMax: 120 },
  satellites: { goodMin: 30, normalMin: 26 },
  hdop: { goodMax: 0.6, normalMax: 0.8 },
  linkRssi: { goodMin: -70, normalMin: -80 },
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

function requireFiniteNumber(value, path) {
  if (typeof value !== "number" || !Number.isFinite(value)) {
    throw new Error(`${path} must be a finite number`);
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

  requireNonEmptyString(input.secret, "secret");
  const thresholds = Object.fromEntries(Object.entries(DEFAULT_THRESHOLDS).map(([key, defaults]) => [
    key, { ...defaults, ...input.thresholds?.[key] },
  ]));
  for (const [key, values] of Object.entries(thresholds)) {
    for (const [name, value] of Object.entries(values)) requireFiniteNumber(value, `thresholds.${key}.${name}`);
  }
  if (thresholds.voltage.normalMin >= thresholds.voltage.goodMin)
    throw new Error("thresholds.voltage.normalMin must be less than goodMin");
  if (thresholds.current.goodMax >= thresholds.current.normalMax)
    throw new Error("thresholds.current.goodMax must be less than normalMax");
  if (thresholds.satellites.normalMin >= thresholds.satellites.goodMin)
    throw new Error("thresholds.satellites.normalMin must be less than goodMin");
  if (thresholds.hdop.goodMax >= thresholds.hdop.normalMax)
    throw new Error("thresholds.hdop.goodMax must be less than normalMax");
  if (thresholds.linkRssi.normalMin >= thresholds.linkRssi.goodMin)
    throw new Error("thresholds.linkRssi.normalMin must be less than goodMin");
  return { server, secret: input.secret.trim(), thresholds };
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
