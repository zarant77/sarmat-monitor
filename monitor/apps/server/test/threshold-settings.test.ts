import { describe, expect, it } from "vitest";
import { thresholdInputSchema } from "@sbm/shared";

const thresholds = {
  warningCellDeltaV: 0.1,
  dangerCellDeltaV: 0.2,
  chargedThresholdPercent: 90,
  dischargedThresholdPercent: 70,
  criticalChargePercent: 20,
  chargeEventDeadbandPercent: 2
};

describe("critical charge threshold", () => {
  it("accepts a critical level below the discharged level", () => {
    expect(thresholdInputSchema.parse(thresholds).criticalChargePercent).toBe(20);
  });

  it("rejects a critical level at or above the discharged level", () => {
    expect(() => thresholdInputSchema.parse({ ...thresholds, criticalChargePercent: 70 })).toThrow();
  });
});
