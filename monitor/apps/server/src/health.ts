import type { HealthState } from "@sbm/shared";

export interface CellHealthResult {
  minCellVoltage: number;
  maxCellVoltage: number;
  cellDelta: number;
  health: HealthState;
}

export function calculateCellHealth(cellVoltages: number[], warning: number, danger: number): CellHealthResult {
  if (cellVoltages.length === 0) throw new Error("At least one cell voltage is required");
  const minCellVoltage = Math.min(...cellVoltages);
  const maxCellVoltage = Math.max(...cellVoltages);
  const cellDelta = Number((maxCellVoltage - minCellVoltage).toFixed(3));
  const health: HealthState = cellDelta >= danger ? "danger" : cellDelta >= warning ? "warning" : "good";
  return { minCellVoltage, maxCellVoltage, cellDelta, health };
}
