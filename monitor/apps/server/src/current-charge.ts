import { calculateBatterySoc, type SocLimits, type SocSample } from "@sbm/shared";

type Reading = { id?: string; sessionId?: string; totalVoltage: string | number; currentAmps?: string | number | null;
  consumedMah?: string | number | null; consumptionComplete?: boolean; measuredAt: Date };

/** Compare sample times, not arrival times: delayed telemetry must not undo a newer check. */
export function currentBatteryCharge(measurement: Reading | Reading[] | undefined, automatic: Reading | Reading[] | undefined,
  limits: SocLimits) {
  const samples: SocSample[] = [];
  for (const [readings, source] of [[measurement, "measurement"], [automatic, "mission_planner"]] as const) {
    for (const [index, reading] of (Array.isArray(readings) ? readings : readings ? [readings] : []).entries()) {
      samples.push({ id: reading.id ?? `${source}-${index}`, source, measuredAt: reading.measuredAt.getTime(),
        totalVoltage: Number(reading.totalVoltage), currentAmps: source === "measurement" ? 0 : reading.currentAmps == null ? null : Number(reading.currentAmps),
        sessionId: reading.sessionId, consumedMah: reading.consumedMah == null ? null : Number(reading.consumedMah),
        consumptionComplete: reading.consumptionComplete });
    }
  }
  const result = calculateBatterySoc(samples, limits);
  if (!result) return null;
  return { totalVoltage: result.totalVoltage, currentAmps: result.currentAmps, chargePercent: result.chargePercent,
    measuredAt: new Date(result.measuredAt).toISOString(), source: result.source, method: result.method,
    consumedMahSinceCheck: result.consumedMahSinceCheck, incomplete: result.incomplete };
}
