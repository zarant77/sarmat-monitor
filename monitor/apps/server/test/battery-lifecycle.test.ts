import { describe, expect, it } from "vitest";
import { batteryInputSchema, batteryUpdateSchema } from "@sbm/shared";
import { assertBatteryOperational, lifecycleChange } from "../src/battery-lifecycle.js";

const now = new Date("2026-09-25T10:00:00Z");
const operational = { state: "ready" as const, archivedAt: null };

describe("battery lifecycle", () => {
  it("archives temporarily, clears activation and allows restoration", () => {
    const archived = lifecycleChange(operational, "archive", now);
    expect(archived).toMatchObject({ state: "storage", archivedAt: now, activeSince: null });
    expect(() => assertBatteryOperational(archived)).toThrow("archived");
    const restored = lifecycleChange(archived, "restore", now);
    expect(restored.archivedAt).toBeNull();
    expect(() => assertBatteryOperational(restored)).not.toThrow();
  });

  it.each([operational, { state: "storage" as const, archivedAt: now }])("retires active and archived batteries permanently", battery => {
    const retired = lifecycleChange(battery, "retirement", now);
    expect(retired).toMatchObject({ state: "retired", archivedAt: now, activeSince: null });
    expect(() => assertBatteryOperational(retired)).toThrow("retired");
    for (const action of ["archive", "restore", "retirement"] as const) {
      expect(() => lifecycleChange(retired, action, now)).toThrow();
    }
  });

  it("rejects duplicate archiving and restoration of operational batteries", () => {
    expect(() => lifecycleChange(operational, "restore", now)).toThrow("not archived");
    expect(() => lifecycleChange({ state: "storage", archivedAt: now }, "archive", now)).toThrow("already archived");
  });

  it("prevents direct retirement through the general create/update forms", () => {
    expect(batteryUpdateSchema.safeParse({ state: "retired" }).success).toBe(false);
    expect(batteryInputSchema.safeParse({ typeId: "9f4a0ad3-c322-4bd2-aeb0-99d58f31852a", serialNumber: "test", label: "test", state: "retired" }).success).toBe(false);
  });
});
