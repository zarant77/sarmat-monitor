import { and, asc, eq } from "drizzle-orm";
import { db } from "./db/index.js";
import { cycleEvents, measurements } from "./db/schema.js";

export interface ChargeStateThresholds {
  chargedThresholdPercent: number;
  dischargedThresholdPercent: number;
  /** Minimum consecutive-measurement change that represents a real event. */
  chargeEventDeadbandPercent?: number;
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
  const ordered = [...history].sort((a, b) => a.measuredAt.getTime() - b.measuredAt.getTime());
  const deadband = thresholds.chargeEventDeadbandPercent ?? 2;
  let previousStable: StableChargeState | null = null;
  let previous: ChargeMeasurement | null = null;
  for (const measurement of ordered) {
    const currentStable = stableState(measurement.chargePercent, thresholds);
    if (previous?.chargePercent != null && measurement.chargePercent != null) {
      const change = measurement.chargePercent - previous.chargePercent;
      if (Math.abs(change) >= deadband) {
        const completesCycle = change > 0 && previousStable === "discharged" && currentStable === "charged";
        inferred.push({ sourceMeasurementId: measurement.id, type: change > 0 ? "charge" : "discharge", cycleDelta: completesCycle ? 1 : 0, occurredAt: measurement.measuredAt });
      }
    }
    if (currentStable) previousStable = currentStable;
    previous = measurement;
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
