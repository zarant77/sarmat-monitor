import { describe, expect, it } from "vitest";
import { calculateBatterySoc, type SocSample } from "@sbm/shared";

const limits = { minVoltage: 42, maxVoltage: 50.4, capacityAh: 54 };
const reading = (seconds: number, voltage: number, current = 0): SocSample => ({
  id: `sample-${seconds}`, measuredAt: seconds * 1000, totalVoltage: voltage,
  currentAmps: current, source: "mission_planner"
});

describe("current battery SOC", () => {
  it("uses latest pack voltage independently of old checks and consumed capacity", () => {
    const oldCheck = { ...reading(0, 50.43), source: "measurement" as const };
    const latest = { ...reading(1, 46.85), consumedMah: 11340, sessionId: "a", consumptionComplete: true };
    expect(calculateBatterySoc([oldCheck, latest], limits)).toMatchObject({ chargePercent: 58, method: "voltage" });
  });
  it("uses configured type voltage limits", () => {
    expect(calculateBatterySoc([reading(1, 46.85)], { ...limits, minVoltage: 40 })?.chargePercent).toBe(66);
  });
  it("compensates instantaneous voltage sag with pack resistance in milliohms", () => {
    expect(calculateBatterySoc([reading(1, 45.9, 65.7)], { ...limits, internalResistanceMilliOhms: 60 }))
      .toMatchObject({ chargePercent: 93, method: "voltage_compensated", incomplete: false });
  });
  it("reflects voltage recovery instead of retaining an old lower estimate", () => {
    expect(calculateBatterySoc([reading(0, 45), reading(1, 50.2)], limits)?.chargePercent).toBe(98);
  });
  it("ignores delivery order and prefers a simultaneous zero-current check", () => {
    const check = { ...reading(1, 50.4, 65), id: "check", source: "measurement" as const };
    expect(calculateBatterySoc([check, reading(0, 45), reading(1, 46)], limits))
      .toMatchObject({ chargePercent: 100, currentAmps: 0, source: "measurement" });
  });
  it("marks uncompensated load uncertain while displaying the current voltage estimate", () => {
    expect(calculateBatterySoc([reading(0, 50.4), { ...reading(1, 45.9, 65.7), armed: true }], limits))
      .toMatchObject({ chargePercent: 46, incomplete: true });
  });
  it("clamps SOC and handles missing readings", () => {
    expect(calculateBatterySoc([reading(1, 60)], limits)?.chargePercent).toBe(100);
    expect(calculateBatterySoc([reading(1, 30)], limits)?.chargePercent).toBe(0);
    expect(calculateBatterySoc([], limits)).toBeNull();
    expect(() => calculateBatterySoc([reading(1, 45)], { ...limits, internalResistanceMilliOhms: -1 })).toThrow();
  });
});
