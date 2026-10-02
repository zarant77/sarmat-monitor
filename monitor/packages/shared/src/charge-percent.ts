/** Default 12S limits, matching the seeded battery type (3.0–4.2 V per cell). */
export const DEFAULT_PACK_MIN_VOLTAGE = 36;
export const DEFAULT_PACK_MAX_VOLTAGE = 50.4;

/** Linear voltage-based SOC estimate, rounded to a whole percent and clamped to 0–100. */
export function voltageToPercent(
  voltage: number,
  minVoltage = DEFAULT_PACK_MIN_VOLTAGE,
  maxVoltage = DEFAULT_PACK_MAX_VOLTAGE
): number {
  if (![voltage, minVoltage, maxVoltage].every(Number.isFinite) || maxVoltage <= minVoltage) {
    throw new Error("Invalid battery voltage limits");
  }
  return Math.round(Math.max(0, Math.min(100, (voltage - minVoltage) / (maxVoltage - minVoltage) * 100)));
}
