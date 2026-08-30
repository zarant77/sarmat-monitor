import { and, asc, eq } from "drizzle-orm";
import { db } from "./db/index.js";
import { cycleEvents, measurements } from "./db/schema.js";

export interface ChargeStateThresholds {
  chargedThresholdPercent: number;
  dischargedThresholdPercent: number;
}

export interface ChargeMeasurement {
  id: string;
  chargePercent: number | null;
  measuredAt: Date;
}

export interface InferredCycleEvent {
  sourceMeasurementId: string;
  type: "charge" | "discharge";
  cycleDelta: 0 | 1;
  occurredAt: Date;
}

type StableChargeState = "charged" | "discharged";

function stableState(chargePercent: number | null, thresholds: ChargeStateThresholds): StableChargeState | null {
  if (chargePercent == null) return null;
  if (chargePercent >= thresholds.chargedThresholdPercent) return "charged";
  if (chargePercent <= thresholds.dischargedThresholdPercent) return "discharged";
  return null;
}

export function inferCycleEvents(history: ChargeMeasurement[], thresholds: ChargeStateThresholds): InferredCycleEvent[] {
  const inferred: InferredCycleEvent[] = [];
  let previousStable: StableChargeState | null = null;
  for (const measurement of [...history].sort((a, b) => a.measuredAt.getTime() - b.measuredAt.getTime())) {
    const current = stableState(measurement.chargePercent, thresholds);
    if (!current) continue;
    if (previousStable === "charged" && current === "discharged") inferred.push({ sourceMeasurementId: measurement.id, type: "discharge", cycleDelta: 0, occurredAt: measurement.measuredAt });
    if (previousStable === "discharged" && current === "charged") inferred.push({ sourceMeasurementId: measurement.id, type: "charge", cycleDelta: 1, occurredAt: measurement.measuredAt });
    previousStable = current;
  }
  return inferred;
}

export async function rebuildInferredCycleEvents(batteryId: string, thresholds: ChargeStateThresholds) {
  const history = await db.select({ id: measurements.id, chargePercent: measurements.chargePercent, measuredAt: measurements.measuredAt })
    .from(measurements).where(eq(measurements.batteryId, batteryId)).orderBy(asc(measurements.measuredAt));
  const inferred = inferCycleEvents(history, thresholds);
  await db.transaction(async tx => {
    await tx.delete(cycleEvents).where(and(eq(cycleEvents.batteryId, batteryId), eq(cycleEvents.inferred, true)));
    if (inferred.length) await tx.insert(cycleEvents).values(inferred.map(event => ({ batteryId, ...event, inferred: true, notes: "" })));
  });
}
