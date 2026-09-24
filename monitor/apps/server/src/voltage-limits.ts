/** Small tolerated per-cell overcharge used by measurement preview and save validation. */
export const CELL_OVERCHARGE_TOLERANCE_V = 0.02;

export function cellVoltageBounds(packMinVoltage: number, packMaxVoltage: number, cellCount: number) {
  return {
    min: packMinVoltage / cellCount,
    max: packMaxVoltage / cellCount + CELL_OVERCHARGE_TOLERANCE_V
  };
}
