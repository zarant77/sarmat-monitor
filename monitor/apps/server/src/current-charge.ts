import { voltageToPercent } from "@sbm/shared";

type Reading = { totalVoltage: string | number; measuredAt: Date };

/** Compare sample times, not arrival times: delayed telemetry must not undo a newer check. */
export function currentBatteryCharge(measurement: Reading | undefined, automatic: Reading | undefined,
  limits: { minVoltage: number; maxVoltage: number }) {
  const useAutomatic = automatic && (!measurement || automatic.measuredAt > measurement.measuredAt);
  const reading = useAutomatic ? automatic : measurement;
  if (!reading) return null;
  const totalVoltage = Number(reading.totalVoltage);
  return { totalVoltage, chargePercent: voltageToPercent(totalVoltage, limits.minVoltage, limits.maxVoltage),
    measuredAt: reading.measuredAt.toISOString(), source: useAutomatic ? "mission_planner" as const : "measurement" as const };
}
