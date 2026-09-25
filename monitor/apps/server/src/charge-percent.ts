/** Charge is derived from the measured voltage and the battery type's current limits. */
export function calculateChargePercent(totalVoltage: number, minVoltage: number, maxVoltage: number): number {
  if (![totalVoltage, minVoltage, maxVoltage].every(Number.isFinite) || maxVoltage <= minVoltage) {
    throw new Error("Invalid battery voltage limits");
  }
  return Math.round(Math.max(0, Math.min(100, (totalVoltage - minVoltage) / (maxVoltage - minVoltage) * 100)));
}
