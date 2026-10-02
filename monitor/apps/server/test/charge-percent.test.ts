import { describe, expect, it } from "vitest";
import { voltageToPercent } from "@sbm/shared";
import { currentBatteryCharge } from "../src/current-charge.js";
import { calculateMeasurementPreview } from "../src/measurement-preview.js";

describe("shared voltage-based SOC", () => {
  it.each([[36, 0], [43.2, 50], [50.4, 100], [30, 0], [55, 100], [40, 28]])(
    "converts %s V to %s percent for 12S", (voltage, percent) => {
      expect(voltageToPercent(voltage)).toBe(percent);
    }
  );

  it("uses configured limits for other battery types", () => {
    expect(voltageToPercent(20, 15, 25)).toBe(50);
  });

  it.each([[NaN, 36, 50.4], [Infinity, 36, 50.4], [40, 50, 36], [40, 36, 36]])(
    "rejects invalid voltage or limits", (voltage, min, max) => {
      expect(() => voltageToPercent(voltage, min, max)).toThrow();
    }
  );

  it.each([3, 3.6, 4.2, 4.24])("agrees for checker and drone readings at %s V/cell", cellVoltage => {
    const cells = Array(6).fill(cellVoltage);
    const preview = calculateMeasurementPreview(cells, cells, 0.1, 0.2, 36, 50.4);
    const reading = { totalVoltage: preview.combinedTotalVoltage, measuredAt: new Date(0) };
    const limits = { minVoltage: 36, maxVoltage: 50.4 };
    expect(preview.cells).toEqual([...cells, ...cells]);
    expect(preview.chargePercent).toBe(voltageToPercent(reading.totalVoltage));
    expect(currentBatteryCharge(reading, undefined, limits)?.chargePercent).toBe(preview.chargePercent);
    expect(currentBatteryCharge(undefined, reading, limits)?.chargePercent).toBe(preview.chargePercent);
  });
});
