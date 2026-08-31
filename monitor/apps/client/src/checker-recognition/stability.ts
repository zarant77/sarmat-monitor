import type { CheckerRecognitionResult, ScannerState } from "./types";

const keyFor = (result: CheckerRecognitionResult) => result.complete ? result.cells.map(value => value!.toFixed(2)).join("|") : null;

export interface StabilityResult {
  state: ScannerState;
  stableCells: number[] | null;
  matches: number;
}

export function evaluateStability(history: CheckerRecognitionResult[], requiredMatches = 3, windowSize = 5): StabilityResult {
  const recent = history.slice(-windowSize);
  const counts = new Map<string, { cells: number[]; count: number }>();
  recent.forEach(result => {
    const key = keyFor(result); if (!key) return;
    const current = counts.get(key) ?? { cells: result.cells as number[], count: 0 };
    current.count += 1; counts.set(key, current);
  });
  const best = [...counts.values()].sort((a, b) => b.count - a.count)[0];
  const current = recent.at(-1);
  if (best && best.count >= requiredMatches && current && keyFor(current) === best.cells.map(value => value.toFixed(2)).join("|")) {
    return { state: "green", stableCells: [...best.cells], matches: best.count };
  }
  const invalid = current?.warnings.some(warning => warning.code === "invalid_voltage" || warning.code === "poor_geometry");
  const partial = current?.lcdDetected && current.cells.some(value => value != null);
  return { state: !invalid && partial ? "yellow" : "red", stableCells: null, matches: best?.count ?? 0 };
}
