import { describe, expect, it } from "vitest";
import { CheckerRecognitionError, combineCheckerReadings, validateModuleRecognition } from "../src/checker-recognition.js";

const config = { minCellVoltage: 2.5, maxCellVoltage: 4.5 };

describe("checker recognition validation", () => {
  it("accepts six plausible cells without a checker Total", () => {
    expect(validateModuleRecognition({ module: "A", cells: [4.2, 4.19, 4.2, 4.2, 4.19, 4.2] }, config)).toMatchObject({ module: "A" });
  });

  it("rejects an invalid number of cells", () => {
    expect(() => validateModuleRecognition({ module: "A", cells: [4.2] }, config)).toThrow(CheckerRecognitionError);
  });

  it("rejects an impossible cell voltage", () => {
    expect(() => validateModuleRecognition({ module: "A", cells: [4.2, 4.2, 8.1, 4.2, 4.2, 4.2] }, config)).toThrowError(/reliably/);
  });

  it("combines A and B in module order and applies health thresholds", () => {
    const a = [4.18, 4.19, 4.17, 4.19, 4.18, 4.18]; const b = Array(6).fill(4.18);
    const result = combineCheckerReadings("set", a, b, 0.1, 0.2, 36, 50.4);
    expect(result.cells).toEqual([...a, ...b]);
    expect(result.moduleATotalVoltage).toBe(25.09);
    expect(result.moduleBTotalVoltage).toBe(25.08);
    expect(result.combinedTotalVoltage).toBe(50.17);
    expect(result.minCellVoltage).toBe(4.17);
    expect(result.maxCellVoltage).toBe(4.19);
    expect(result.cellDelta).toBe(0.02);
    expect(result.health).toBe("good");
  });
});
