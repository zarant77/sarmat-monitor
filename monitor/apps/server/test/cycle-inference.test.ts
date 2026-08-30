import { describe, expect, it } from "vitest";
import { inferCycleEvents } from "../src/cycle-inference.js";

const thresholds = { chargedThresholdPercent: 90, dischargedThresholdPercent: 20 };
const measurement = (id: string, chargePercent: number, minute: number) => ({ id, chargePercent, measuredAt: new Date(`2026-01-01T00:${String(minute).padStart(2, "0")}:00Z`) });

describe("measurement-derived cycle history", () => {
  it("records usage when a charged battery becomes discharged", () => {
    expect(inferCycleEvents([measurement("charged", 95, 0), measurement("discharged", 18, 10)], thresholds)).toEqual([
      expect.objectContaining({ sourceMeasurementId: "discharged", type: "discharge", cycleDelta: 0 })
    ]);
  });

  it("counts one cycle when a discharged battery becomes charged", () => {
    expect(inferCycleEvents([measurement("discharged", 15, 0), measurement("charged", 97, 10)], thresholds)).toEqual([
      expect.objectContaining({ sourceMeasurementId: "charged", type: "charge", cycleDelta: 1 })
    ]);
  });

  it("does not create events for repeated measurements in the same stable state", () => {
    expect(inferCycleEvents([
      measurement("charged-1", 94, 0), measurement("charged-2", 99, 5),
      measurement("discharged-1", 19, 10), measurement("discharged-2", 12, 15)
    ], thresholds)).toEqual([expect.objectContaining({ sourceMeasurementId: "discharged-1", type: "discharge" })]);
  });

  it("ignores intermediate readings without losing the last stable state", () => {
    const events = inferCycleEvents([
      measurement("discharged", 10, 0), measurement("partial-1", 35, 5), measurement("partial-2", 72, 10), measurement("charged", 92, 15), measurement("charged-again", 96, 20)
    ], thresholds);
    expect(events).toEqual([expect.objectContaining({ sourceMeasurementId: "charged", type: "charge", cycleDelta: 1 })]);
  });

  it("uses configurable boundaries", () => {
    const history = [measurement("first", 25, 0), measurement("second", 85, 10)];
    expect(inferCycleEvents(history, thresholds)).toEqual([]);
    expect(inferCycleEvents(history, { chargedThresholdPercent: 80, dischargedThresholdPercent: 30 })).toEqual([
      expect.objectContaining({ sourceMeasurementId: "second", type: "charge", cycleDelta: 1 })
    ]);
  });
});
