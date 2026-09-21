import { describe, expect, it } from "vitest";
import { nextActiveBatteryId } from "../src/active-battery.js";
import { inferCycleEvents } from "../src/cycle-inference.js";

describe("crew active battery toggle", () => {
  it("activates a battery when none is active", () => {
    expect(nextActiveBatteryId(null, "battery-a")).toBe("battery-a");
  });

  it("toggles the currently active battery off", () => {
    expect(nextActiveBatteryId("battery-a", "battery-a")).toBeNull();
  });

  it("switches directly to another battery", () => {
    expect(nextActiveBatteryId("battery-a", "battery-b")).toBe("battery-b");
  });

  it("does not create charge, discharge, or cycle events", () => {
    nextActiveBatteryId("battery-a", "battery-b");
    expect(inferCycleEvents([{ id: "only-measurement", chargePercent: 98, measuredAt: new Date() }], {
      chargedThresholdPercent: 90, dischargedThresholdPercent: 50, chargeEventDeadbandPercent: 2
    })).toEqual([]);
  });
});
