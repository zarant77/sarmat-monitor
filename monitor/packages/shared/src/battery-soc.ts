import { voltageToPercent } from "./charge-percent.js";

export type SocMethod = "voltage" | "voltage_compensated" | "consumption";
export interface SocSample {
  id: string;
  measuredAt: number;
  totalVoltage: number;
  currentAmps: number | null;
  source: "measurement" | "mission_planner";
  sessionId?: string;
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

/** Current SOC uses only the newest pack reading; cell health and consumption history are separate. */
export function calculateBatterySoc(samples: SocSample[], limits: SocLimits) {
  const latest = [...new Map(samples.map(sample => [sample.id, sample])).values()]
    .sort((a, b) => a.measuredAt - b.measuredAt ||
      (a.source === b.source ? a.id.localeCompare(b.id) : a.source === "measurement" ? 1 : -1)).pop();
  const resistance = limits.internalResistanceMilliOhms ?? 0;
  if (!Number.isFinite(resistance) || resistance < 0) throw new Error("Invalid battery resistance");
  if (!latest) return null;
  const currentAmps = latest.source === "measurement" ? 0 : latest.currentAmps;
  const compensated = latest.source === "mission_planner" && currentAmps != null && resistance > 0;
  const voltage = latest.totalVoltage + (compensated ? currentAmps! * resistance / 1000 : 0);
  const underLoad = latest.armed === true || (currentAmps != null &&
    currentAmps > Math.max(2, (limits.capacityAh ?? 20) * 0.05));
  return { ...latest, currentAmps,
    chargePercent: voltageToPercent(voltage, limits.minVoltage, limits.maxVoltage),
    method: (compensated ? "voltage_compensated" : "voltage") as SocMethod,
    consumedMahSinceCheck: 0,
    incomplete: latest.source === "mission_planner" && (currentAmps == null || (underLoad && !compensated)) };
}
