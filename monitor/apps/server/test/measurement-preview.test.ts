import { describe, expect, it } from "vitest";
import { measurementInputSchema } from "@sbm/shared";
import { calculateMeasurementPreview } from "../src/measurement-preview.js";

describe("structured measurement boundary", () => {
  it("accepts only primary cell values and ignores client-derived fields", () => {
    const cells = [...Array(6).fill(4.2), 4.19, 4.2, 4.2, 4.2, 4.2, 4.2];
    const parsed = measurementInputSchema.parse({ cellVoltages: cells, notes: "confirmed", totalVoltage: 1, health: "danger", image: "data" });
    expect(parsed).toEqual({ cellVoltages: cells, notes: "confirmed" });
  });

  it("recalculates A+B totals and health on the server", () => {
    const preview = calculateMeasurementPreview(Array(6).fill(4.2), [4.19, 4.2, 4.2, 4.2, 4.2, 4.2], .1, .2, 36, 50.4);
    expect(preview).toMatchObject({ cells: [...Array(6).fill(4.2), 4.19, 4.2, 4.2, 4.2, 4.2, 4.2], moduleATotalVoltage: 25.2, moduleBTotalVoltage: 25.19, combinedTotalVoltage: 50.39, minCellVoltage: 4.19, maxCellVoltage: 4.2, health: "good" });
  });

  it("rejects invalid module counts and battery-type ranges", () => {
    expect(() => calculateMeasurementPreview(Array(5).fill(4.2), Array(6).fill(4.2), .1, .2, 36, 50.4)).toThrow("exactly 6");
    expect(() => calculateMeasurementPreview(Array(6).fill(4.5), Array(6).fill(4.2), .1, .2, 36, 50.4)).toThrow("outside");
  });
});
