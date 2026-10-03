import { describe, expect, it } from "vitest";
import { currentBatteryCharge } from "../src/current-charge.js";

const limits = { minVoltage: 36, maxVoltage: 50.4 };
const reading = (voltage: number, seconds: number) => ({ totalVoltage: String(voltage), measuredAt: new Date(seconds * 1000) });
describe("current battery charge", () => {
  it("preserves sample current and distinguishes unknown drone current from a zero-current check", () => {
    expect(currentBatteryCharge(reading(40, 1), undefined, limits)?.currentAmps).toBe(0);
    expect(currentBatteryCharge(undefined, reading(40, 1), limits)?.currentAmps).toBeNull();
    expect(currentBatteryCharge(reading(50.4, 0), { ...reading(40, 1), currentAmps: "12.345" }, limits)?.currentAmps).toBe(12.345);
    expect(currentBatteryCharge(undefined, { ...reading(40, 1), currentAmps: "12.345", armed: true }, limits)).toBeNull();
  });
  it("uses newer automatic voltage instead of a full cell check", () => {
    expect(currentBatteryCharge(reading(50.4, 1), reading(40, 2), limits)).toMatchObject({ chargePercent: 28, totalVoltage: 40, source: "mission_planner" });
  });
  it("a newer recharge check supersedes automatic discharge, regardless of delivery order", () => {
    expect(currentBatteryCharge(reading(50.4, 3), reading(40, 2), limits)).toMatchObject({ chargePercent: 100, source: "measurement" });
  });
  it("supports telemetry-only batteries and no readings", () => {
    expect(currentBatteryCharge(undefined, reading(40, 1), limits)?.chargePercent).toBe(28);
    expect(currentBatteryCharge(undefined, undefined, limits)).toBeNull();
  });
  it("prefers a cell check at equal timestamps and recalculates with current type limits", () => {
    expect(currentBatteryCharge(reading(40, 1), reading(50, 1), limits)?.source).toBe("measurement");
    expect(currentBatteryCharge(undefined, reading(40, 1), { minVoltage: 30, maxVoltage: 50 })?.chargePercent).toBe(50);
  });
});
