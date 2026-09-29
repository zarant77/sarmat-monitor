export function flightTimeToSeconds(hours: number, minutes: number): number {
  if (!Number.isInteger(hours) || hours < 0 || !Number.isInteger(minutes) || minutes < 0 || minutes > 59) {
    throw new Error("Invalid flight time");
  }
  return hours * 3600 + minutes * 60;
}

export function splitFlightTime(totalSeconds: number): { hours: number; minutes: number } {
  const safeSeconds = Number.isFinite(totalSeconds) ? Math.max(0, Math.floor(totalSeconds)) : 0;
  return { hours: Math.floor(safeSeconds / 3600), minutes: Math.floor((safeSeconds % 3600) / 60) };
}

export function formatFlightTime(totalSeconds: number, hourLabel: string, minuteLabel: string): string {
  const { hours, minutes } = splitFlightTime(totalSeconds);
  return `${hours} ${hourLabel} ${minutes} ${minuteLabel}`;
}
