import { existsSync, readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";

const candidates = [
  new URL("../../shared/telemetry-thresholds.json", import.meta.url),
  new URL("../shared/telemetry-thresholds.json", import.meta.url),
];

function finite(value, path) {
  if (typeof value !== "number" || !Number.isFinite(value)) throw new Error(`${path} must be a finite number`);
}

function validateMinimum(value, path) {
  finite(value?.goodMin, `${path}.goodMin`);
  finite(value?.normalMin, `${path}.normalMin`);
  if (value.normalMin >= value.goodMin) throw new Error(`${path}.normalMin must be less than goodMin`);
}

function validateMaximum(value, path) {
  finite(value?.goodMax, `${path}.goodMax`);
  finite(value?.normalMax, `${path}.normalMax`);
  if (value.goodMax >= value.normalMax) throw new Error(`${path}.goodMax must be less than normalMax`);
}

function load() {
  const source = candidates.find((candidate) => existsSync(fileURLToPath(candidate)));
  if (!source) throw new Error("shared/telemetry-thresholds.json was not found");
  const thresholds = JSON.parse(readFileSync(source, "utf8"));
  validateMinimum(thresholds.voltage, "voltage");
  validateMaximum(thresholds.current, "current");
  validateMinimum(thresholds.satellites, "satellites");
  validateMaximum(thresholds.hdop, "hdop");
  validateMinimum(thresholds.linkRssi, "linkRssi");
  validateMaximum(thresholds.distanceToHome, "distanceToHome");
  return Object.freeze(thresholds);
}

export const SHARED_THRESHOLDS = load();
