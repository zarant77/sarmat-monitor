import { describe, expect, it } from "vitest";
import { calculateBatterySoc, type SocSample } from "@sbm/shared";

const limits = { minVoltage: 36, maxVoltage: 50.4, capacityAh: 20 };
const check = (seconds: number, voltage = 50.4): SocSample => ({ id: `check-${seconds}`, measuredAt: seconds * 1000,
  totalVoltage: voltage, currentAmps: 0, source: "measurement" });
const sample = (seconds: number, consumedMah: number | null, voltage = 48.41, sessionId = "a", complete = true): SocSample => ({
  id: `sample-${sessionId}-${seconds}`, measuredAt: seconds * 1000, totalVoltage: voltage, currentAmps: 10,
  source: "mission_planner", sessionId, consumedMah, consumptionComplete: complete });

describe("battery SOC replay", () => {
  it("subtracts mAh and ignores voltage recovery after landing and reconnect", () => {
    expect(calculateBatterySoc([check(0), sample(1, 0), sample(2, 4000), sample(3, 4000, 49.21),
      sample(4, 0, 49.21, "b"), sample(5, 2000, 49.4, "b")], limits))
      .toMatchObject({ chargePercent: 70, consumedMahSinceCheck: 6000, method: "consumption", incomplete: false });
  });
  it("is independent of delivery order, duplicate IDs and decreasing counters", () => {
    const readings = [check(0), sample(1, 0), sample(2, 4000), sample(3, 3000), sample(4, 5000)];
    expect(calculateBatterySoc([...readings].reverse().concat(readings[2]), limits)?.chargePercent).toBe(75);
  });
  it("retains fractions rather than rounding each telemetry interval", () => {
    const readings = [check(0), ...Array.from({ length: 100 }, (_, i) => sample(i + 1, (i + 1) * 2))];
    expect(calculateBatterySoc(readings, limits)?.chargePercent).toBe(99);
  });
  it("a new checker reading resets the anchor without recounting old session consumption", () => {
    expect(calculateBatterySoc([check(0), sample(1, 4000), check(2, 43.2), sample(3, 6000)], limits))
      .toMatchObject({ chargePercent: 40, consumedMahSinceCheck: 2000 });
  });
  it("a confirmed full checker reading restores 100 percent", () => {
    expect(calculateBatterySoc([check(0), sample(1, 10000), check(2)], limits)?.chargePercent).toBe(100);
  });
  it("uses actual capacity provided by the caller", () => {
    expect(calculateBatterySoc([check(0), sample(1, 4000)], { ...limits, capacityAh: 10 })?.chargePercent).toBe(60);
  });
  it("legacy readings without consumption can lower SOC but cannot recharge it", () => {
    expect(calculateBatterySoc([check(0), { ...sample(1, null, 48.41), currentAmps: 1 }, { ...sample(2, null, 49.21), currentAmps: 1 }], limits))
      .toMatchObject({ chargePercent: 86, method: "voltage", incomplete: true });
  });
  it("compensates fallback voltage using pack mΩ and amperes", () => {
    expect(calculateBatterySoc([sample(0, null, 42.2)], { ...limits, internalResistanceMilliOhms: 100 }))
      .toMatchObject({ chargePercent: 50, method: "voltage_compensated", incomplete: true });
  });
  it("marks gaps uncertain while retaining measured consumption", () => {
    expect(calculateBatterySoc([check(0), sample(1, 4000, 49, "a", false), sample(2, 5000, 50)], limits))
      .toMatchObject({ chargePercent: 75, incomplete: true, consumedMahSinceCheck: 5000 });
  });
  it("bootstraps telemetry-only batteries without subtracting consumption twice", () => {
    expect(calculateBatterySoc([{ ...sample(0, 4000, 43.2), currentAmps: 1 }, sample(1, 6000, 49.21)], limits)?.chargePercent).toBe(40);
  });
  it("does not turn takeoff voltage sag into lost capacity when consumption has gaps", () => {
    const pack = { minVoltage: 42, maxVoltage: 50.4, capacityAh: 54 };
    expect(calculateBatterySoc([
      { ...sample(0, 0, 50.2, "a", false), currentAmps: 1.3, armed: false },
      { ...sample(15, 250, 45.3, "a", false), currentAmps: 65.7, armed: true },
      { ...sample(30, 500, 45, "a", false), currentAmps: 70, armed: true },
      { ...sample(40, 500, 49.2, "a", false), currentAmps: 1.3, armed: false }
    ], pack)).toMatchObject({ chargePercent: 97, consumedMahSinceCheck: 500, method: "consumption", incomplete: true });
  });
  it("holds an existing anchor during load if no consumption counter is available", () => {
    expect(calculateBatterySoc([check(0), { ...sample(1, null, 42), currentAmps: 65, armed: true }], limits))
      .toMatchObject({ chargePercent: 100, incomplete: true });
    expect(calculateBatterySoc([check(0), { ...sample(1, null, 42), currentAmps: 65 }], limits)?.chargePercent).toBe(100);
  });
  it("does not bootstrap an uncompensated SOC from a loaded voltage", () => {
    expect(calculateBatterySoc([{ ...sample(0, 0, 45.3), currentAmps: 65, armed: true }], limits)).toBeNull();
    expect(calculateBatterySoc([{ ...sample(0, 0, 45.3), currentAmps: 65, armed: true }, check(1)], limits)?.chargePercent).toBe(100);
  });
  it("prefers checker at equal timestamps and clamps an exhausted pack", () => {
    expect(calculateBatterySoc([sample(0, 0, 36), check(0)], limits)?.chargePercent).toBe(100);
    expect(calculateBatterySoc([check(0), sample(1, 30000)], limits)?.chargePercent).toBe(0);
  });
  it("returns unknown with no readings and rejects invalid calibration", () => {
    expect(calculateBatterySoc([], limits)).toBeNull();
    expect(() => calculateBatterySoc([check(0)], { ...limits, capacityAh: 0 })).toThrow();
    expect(() => calculateBatterySoc([check(0)], { ...limits, internalResistanceMilliOhms: -1 })).toThrow();
  });
});
