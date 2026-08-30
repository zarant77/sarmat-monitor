import type { CheckerCombinedPreview, CheckerModule, CheckerModuleRecognition } from "@sbm/shared";
import { calculateCellHealth } from "./health.js";

export interface RecognitionIssue {
  code: "invalid_image" | "unreadable_digit" | "invalid_cell_count" | "impossible_cell_voltage" | "provider_unavailable" | "model_unavailable" | "invalid_model_response";
  field?: string;
  message: string;
  value?: number | null;
}

export class CheckerRecognitionError extends Error {
  readonly statusCode = 422;
  readonly code = "CHECKER_RECOGNITION_FAILED";
  constructor(readonly issues: RecognitionIssue[], readonly partial?: unknown) {
    const providerIssue = issues.find(issue => issue.code === "provider_unavailable" || issue.code === "model_unavailable");
    super(providerIssue?.message ?? "Checker display could not be recognized reliably");
  }
}

export interface CheckerRecognitionConfig {
  minCellVoltage: number;
  maxCellVoltage: number;
}

const envNumber = (name: string, fallback: number) => {
  const value = Number(process.env[name]);
  return Number.isFinite(value) ? value : fallback;
};

export const checkerRecognitionConfig: CheckerRecognitionConfig = {
  minCellVoltage: envNumber("CHECKER_CELL_MIN_VOLTAGE", 2.5),
  maxCellVoltage: envNumber("CHECKER_CELL_MAX_VOLTAGE", 4.5)
};

export function validateModuleRecognition(reading: { module: CheckerModule; cells: number[] }, config: CheckerRecognitionConfig = checkerRecognitionConfig) {
  const issues: RecognitionIssue[] = [];
  if (reading.cells.length !== 6) issues.push({ code: "invalid_cell_count", field: "cells", message: `Expected 6 cells, received ${reading.cells.length}` });
  reading.cells.forEach((voltage, index) => {
    if (!Number.isFinite(voltage) || voltage < config.minCellVoltage || voltage > config.maxCellVoltage) issues.push({ code: "impossible_cell_voltage", field: `cells.${index}`, value: voltage, message: `Cell ${index + 1} is outside the configured voltage range` });
  });
  if (issues.length) throw new CheckerRecognitionError(issues, reading);
  return reading;
}

export interface CheckerRecognizer {
  recognize(image: Buffer, module: CheckerModule, limits?: CheckerRecognitionConfig): Promise<CheckerModuleRecognition>;
}

export function combineCheckerReadings(photoSetId: string, cellsA: number[], cellsB: number[], warning: number, danger: number, packMinVoltage?: number, packMaxVoltage?: number): CheckerCombinedPreview {
  if (cellsA.length !== 6 || cellsB.length !== 6) throw new CheckerRecognitionError([{ code: "invalid_cell_count", field: "cells", message: "Both modules must contain exactly 6 cells" }]);
  const cellLimits = packMinVoltage != null && packMaxVoltage != null
    ? { minCellVoltage: packMinVoltage / 12, maxCellVoltage: packMaxVoltage / 12 }
    : checkerRecognitionConfig;
  validateModuleRecognition({ module: "A", cells: cellsA }, cellLimits);
  validateModuleRecognition({ module: "B", cells: cellsB }, cellLimits);
  const cells = [...cellsA, ...cellsB];
  const health = calculateCellHealth(cells, warning, danger);
  const moduleATotalVoltage = Math.round(cellsA.reduce((sum, voltage) => sum + voltage, 0) * 1000) / 1000;
  const moduleBTotalVoltage = Math.round(cellsB.reduce((sum, voltage) => sum + voltage, 0) * 1000) / 1000;
  const combinedTotalVoltage = Math.round((moduleATotalVoltage + moduleBTotalVoltage) * 1000) / 1000;
  const chargePercent = packMinVoltage != null && packMaxVoltage != null && packMaxVoltage > packMinVoltage
    ? Math.round(Math.max(0, Math.min(100, (combinedTotalVoltage - packMinVoltage) / (packMaxVoltage - packMinVoltage) * 100)))
    : null;
  return { photoSetId, cells, moduleATotalVoltage, moduleBTotalVoltage, combinedTotalVoltage, chargePercent, ...health, warningThresholdV: warning, dangerThresholdV: danger };
}
