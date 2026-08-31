import type { CheckerRecognitionResult, ScannerState } from "./types";

export interface StabilityResult {
  state: ScannerState;
  stableCells: number[] | null;
  observedCells: Array<number | null>;
  matches: number;
}

export function evaluateStability(history: CheckerRecognitionResult[], requiredMatches = 3, windowSize = 5): StabilityResult {
  const recent = history.slice(-windowSize);
  const candidateCells: Array<number | null> = []; const cellMatches: number[] = [];
  for (let cellIndex = 0; cellIndex < 6; cellIndex += 1) {
    const counts = new Map<string, { value: number; count: number }>();
    recent.forEach(result => {
      const value = result.cells[cellIndex]; if (value == null) return;
      const key = value.toFixed(2); const current = counts.get(key) ?? { value, count: 0 };
      current.count += 1; counts.set(key, current);
    });
    const best = [...counts.values()].sort((a, b) => b.count - a.count)[0];
    candidateCells.push(best?.value ?? null); cellMatches.push(best?.count ?? 0);
  }
  if (candidateCells.every((value, index) => value != null && cellMatches[index] >= requiredMatches)) {
    return { state: "green", stableCells: candidateCells as number[], observedCells: candidateCells, matches: Math.min(...cellMatches) };
  }
  const current = recent.at(-1);
  const invalid = current?.warnings.some(warning => warning.code === "invalid_voltage" || warning.code === "poor_geometry");
  const partial = current?.lcdDetected && current.cells.some(value => value != null);
  return { state: !invalid && partial ? "yellow" : "red", stableCells: null, observedCells: candidateCells, matches: Math.min(...cellMatches) };
}
