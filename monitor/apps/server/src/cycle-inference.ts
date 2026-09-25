import { and, asc, eq } from "drizzle-orm";
import { db } from "./db/index.js";
import { calculateChargePercent } from "./charge-percent.js";
import { batteries, batteryTypes, cycleEvents, measurements } from "./db/schema.js";

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

export type ChargeTransaction = Parameters<Parameters<typeof db.transaction>[0]>[0];

export async function rebuildInferredCycleEvents(batteryId: string, thresholds: ChargeStateThresholds, transaction?: ChargeTransaction) {
  const rebuild = async (tx: ChargeTransaction) => {
    // Serialize rebuilds and read history after obtaining the battery lock.
    const [battery] = await tx.select().from(batteries).where(eq(batteries.id, batteryId)).for("update");
    if (!battery) return;
    const [type] = await tx.select().from(batteryTypes).where(eq(batteryTypes.id, battery.typeId));
    const history = await tx.select({ id: measurements.id, totalVoltage: measurements.totalVoltage, measuredAt: measurements.measuredAt })
      .from(measurements).where(eq(measurements.batteryId, batteryId)).orderBy(asc(measurements.measuredAt), asc(measurements.id));
    const inferred = inferCycleEvents(history.map(row => ({
      id: row.id, measuredAt: row.measuredAt,
      chargePercent: calculateChargePercent(Number(row.totalVoltage), Number(type.minVoltage), Number(type.maxVoltage))
    })), thresholds);
    await tx.delete(cycleEvents).where(and(eq(cycleEvents.batteryId, batteryId), eq(cycleEvents.inferred, true)));
    if (inferred.length) await tx.insert(cycleEvents).values(inferred.map(event => ({ batteryId, ...event, inferred: true, notes: "" })));
  };
  if (transaction) await rebuild(transaction);
  else await db.transaction(rebuild);
}
