/** Pure toggle rule used by the transactional active-battery endpoint. */
export function nextActiveBatteryId(currentActiveBatteryId: string | null, targetBatteryId: string): string | null {
  return currentActiveBatteryId === targetBatteryId ? null : targetBatteryId;
}
