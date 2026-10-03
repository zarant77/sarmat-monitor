import { voltageToPercent } from "./charge-percent.js";

export type SocMethod = "voltage" | "voltage_compensated" | "consumption";
export interface SocSample {
  id: string;
  measuredAt: number;
  totalVoltage: number;
  currentAmps: number | null;
  source: "measurement" | "mission_planner";
  sessionId?: string;
  /** Cumulative mAh since this session began, never a per-request delta. */
  consumedMah?: number | null;
  consumptionComplete?: boolean;
  armed?: boolean | null;
}
export interface SocLimits {
  minVoltage: number;
  maxVoltage: number;
  capacityAh?: number;
  internalResistanceMilliOhms?: number | null;
}

/** Replay primary readings in sample-time order; delivery order and retries cannot change SOC. */
export function calculateBatterySoc(samples: SocSample[], limits: SocLimits) {
  const ordered = [...new Map(samples.map(sample => [sample.id, sample])).values()]
    .sort((a, b) => a.measuredAt - b.measuredAt ||
      (a.source === b.source ? a.id.localeCompare(b.id) : a.source === "measurement" ? 1 : -1));
  const counters = new Map<string, number>();
  let percent: number | null = null;
  let consumedMahSinceCheck = 0;
  let incomplete = false;
  let method: SocMethod = "voltage";
  let latest: SocSample | undefined;
  const capacityMah = limits.capacityAh == null ? null : limits.capacityAh * 1000;
  if (capacityMah != null && (!Number.isFinite(capacityMah) || capacityMah <= 0)) throw new Error("Invalid battery capacity");
  const resistance = limits.internalResistanceMilliOhms ?? 0;
  if (!Number.isFinite(resistance) || resistance < 0) throw new Error("Invalid battery resistance");
  for (const sample of ordered) {
    latest = sample;
    const compensated = sample.source === "mission_planner" && sample.currentAmps != null && resistance > 0;
    const voltage = sample.totalVoltage + (compensated ? sample.currentAmps! * resistance / 1000 : 0);
    const voltagePercent = voltageToPercent(voltage, limits.minVoltage, limits.maxVoltage);
    // Older plugins have no ARMED flag; a substantial current still identifies load.
    const underLoad = sample.armed === true || (sample.currentAmps != null &&
      sample.currentAmps > Math.max(2, (limits.capacityAh ?? 20) * 0.05));
    if (sample.source === "measurement") {
      percent = voltagePercent;
      consumedMahSinceCheck = 0;
      incomplete = false;
      method = "voltage";
      continue;
    }
    const counter = sample.consumedMah;
    let delta = 0;
    const metered = counter != null && Number.isFinite(counter) && counter >= 0 && sample.sessionId != null;
    if (metered) {
      const previous = counters.get(sample.sessionId!) ?? 0;
      // Decreasing cumulative values are stale/corrupt, never a new discharge.
      delta = Math.max(0, counter! - previous);
      counters.set(sample.sessionId!, Math.max(previous, counter!));
    }
    if (percent == null) {
      if (underLoad && !compensated) { incomplete = true; continue; }
      // With no earlier anchor, this voltage already reflects consumption before this sample.
      percent = voltagePercent;
      method = compensated ? "voltage_compensated" : "voltage";
      incomplete = true;
      continue;
    }
    if (metered && capacityMah != null) {
      consumedMahSinceCheck += delta;
      percent = Math.max(0, percent - delta / capacityMah * 100);
      method = "consumption";
      incomplete = incomplete || sample.consumptionComplete !== true;
    } else {
      incomplete = true;
      // A high-load voltage is sag, not evidence of a matching loss of capacity.
      if (underLoad) continue;
      // A resting/reconnect voltage may lower an uncertain estimate, but cannot recharge it.
      percent = Math.min(percent, voltagePercent);
      method = compensated ? "voltage_compensated" : "voltage";
    }
  }
  if (!latest || percent == null) return null;
  return { ...latest, chargePercent: Math.round(Math.max(0, Math.min(100, percent))),
    method, consumedMahSinceCheck, incomplete };
}
