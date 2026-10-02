import type { Battery } from "@sbm/shared";

export const DEFAULT_DISCHARGED_THRESHOLD_PERCENT = 50;

export function batteryCharge(battery: Pick<Battery, "currentCharge" | "latestMeasurement">) {
  return battery.currentCharge !== undefined ? battery.currentCharge : battery.latestMeasurement;
}

export function isBatteryDischarged(
  battery: Pick<Battery, "state" | "archivedAt" | "latestMeasurement" | "currentCharge">,
  dischargedThresholdPercent: number
) {
  const chargePercent = batteryCharge(battery)?.chargePercent;
  return !battery.archivedAt
    && battery.state === "ready"
    && chargePercent != null
    && chargePercent <= dischargedThresholdPercent;
}
