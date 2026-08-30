import { describe, expect, it } from "vitest";
import { calculateCellHealth } from "../src/health.js";

describe("calculateCellHealth", () => {
  it("calculates cell statistics", () => {
    expect(calculateCellHealth([4.12, 4.08, 4.1], 0.1, 0.2)).toEqual({
      minCellVoltage: 4.08, maxCellVoltage: 4.12, cellDelta: 0.04, health: "good"
    });
  });
  it("uses configurable warning and danger boundaries", () => {
    expect(calculateCellHealth([4.1, 3.98], 0.1, 0.2).health).toBe("warning");
    expect(calculateCellHealth([4.2, 3.9], 0.1, 0.2).health).toBe("danger");
  });
});
