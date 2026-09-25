import type { BatteryState } from "@sbm/shared";

type LifecycleBattery = { state: BatteryState; archivedAt: Date | null };
export type LifecycleAction = "archive" | "restore" | "retirement";

function conflict(message: string): never {
  throw Object.assign(new Error(message), { statusCode: 409 });
}

export function assertBatteryOperational(battery: LifecycleBattery) {
  if (battery.state === "retired") conflict("Battery is retired");
  if (battery.archivedAt) conflict("Battery is archived");
}

export function lifecycleChange(battery: LifecycleBattery, action: LifecycleAction, now: Date) {
  if (battery.state === "retired") conflict("Retired batteries cannot be restored or operated");
  if (action === "restore") {
    if (!battery.archivedAt) conflict("Battery is not archived");
    return { state: "storage" as const, archivedAt: null, activeSince: null, updatedAt: now };
  }
  if (action === "archive" && battery.archivedAt) conflict("Battery is already archived");
  return { state: action === "retirement" ? "retired" as const : "storage" as const,
    archivedAt: battery.archivedAt ?? now, activeSince: null, updatedAt: now };
}
