import type { Battery } from "@sbm/shared";

export const DEFAULT_DISCHARGED_THRESHOLD_PERCENT = 50;

export function isBatteryDischarged(
  battery: Pick<Battery, "state" | "archivedAt" | "latestMeasurement">,
  dischargedThresholdPercent: number
) {
  const chargePercent = battery.latestMeasurement?.chargePercent;
  return !battery.archivedAt
    && battery.state === "ready"
    && chargePercent != null
    && chargePercent <= dischargedThresholdPercent;
}
