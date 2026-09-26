import { describe, expect, it } from "vitest";
import { isBatteryDischarged } from "./battery-display";

const battery = (state: string, chargePercent: number | null, archivedAt: string | null = null) => ({
  state,
  archivedAt,
  latestMeasurement: chargePercent == null ? null : { chargePercent }
}) as Parameters<typeof isBatteryDischarged>[0];

describe("isBatteryDischarged", () => {
  it("uses the configured inclusive discharge threshold", () => {
    expect(isBatteryDischarged(battery("ready", 50), 50)).toBe(true);
    expect(isBatteryDischarged(battery("ready", 51), 50)).toBe(false);
  });

  it("does not replace explicit operational or archived states", () => {
    expect(isBatteryDischarged(battery("charging", 20), 50)).toBe(false);
    expect(isBatteryDischarged(battery("service", 20), 50)).toBe(false);
    expect(isBatteryDischarged(battery("ready", 20, "2026-09-26T12:00:00Z"), 50)).toBe(false);
  });

  it("requires a measurement", () => {
    expect(isBatteryDischarged(battery("ready", null), 50)).toBe(false);
  });
});
