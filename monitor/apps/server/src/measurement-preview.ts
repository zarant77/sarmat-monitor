import type { MeasurementPreview } from "@sbm/shared";
import { calculateCellHealth } from "./health.js";

function validateModule(cells: number[], minCellVoltage: number, maxCellVoltage: number) {
  if (cells.length !== 6) throw Object.assign(new Error("Both modules must contain exactly 6 cells"), { statusCode: 400 });
  cells.forEach((voltage, index) => {
    if (!Number.isFinite(voltage) || voltage < minCellVoltage || voltage > maxCellVoltage) {
      throw Object.assign(new Error(`Cell ${index + 1} is outside the battery type voltage range`), { statusCode: 400 });
    }
  });
}

export function calculateMeasurementPreview(
  cellsA: number[], cellsB: number[], warning: number, danger: number,
  packMinVoltage: number, packMaxVoltage: number
): MeasurementPreview {
  const minCellVoltage = packMinVoltage / 12;
  const maxCellVoltage = packMaxVoltage / 12;
  validateModule(cellsA, minCellVoltage, maxCellVoltage);
  validateModule(cellsB, minCellVoltage, maxCellVoltage);
  const cells = [...cellsA, ...cellsB];
  const health = calculateCellHealth(cells, warning, danger);
  const moduleATotalVoltage = Math.round(cellsA.reduce((sum, voltage) => sum + voltage, 0) * 1000) / 1000;
  const moduleBTotalVoltage = Math.round(cellsB.reduce((sum, voltage) => sum + voltage, 0) * 1000) / 1000;
  const combinedTotalVoltage = Math.round((moduleATotalVoltage + moduleBTotalVoltage) * 1000) / 1000;
  const chargePercent = Math.round(Math.max(0, Math.min(100, (combinedTotalVoltage - packMinVoltage) / (packMaxVoltage - packMinVoltage) * 100)));
  return { cells, moduleATotalVoltage, moduleBTotalVoltage, combinedTotalVoltage, chargePercent, ...health, warningThresholdV: warning, dangerThresholdV: danger };
}
