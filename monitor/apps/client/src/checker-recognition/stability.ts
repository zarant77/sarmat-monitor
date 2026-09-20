import type { CheckerRecognitionResult, ScannerState } from "./types";

export interface StabilityResult {
  state: ScannerState;
  stableCells: number[] | null;
  observedCells: Array<number | null>;
  matches: number;
}

export const REQUIRED_CELL_MATCHES = 4;
export const REQUIRED_CELL_LEAD = 2;
export const REVIEW_MINIMUM_FRAMES = 12;
export const REVIEW_MINIMUM_MATCHES = 2;

export function cellsReadyForReview(
  stability: StabilityResult,
  frameCount: number,
  minimumFrames = REVIEW_MINIMUM_FRAMES,
  minimumMatches = REVIEW_MINIMUM_MATCHES
) {
  return frameCount >= minimumFrames && stability.matches >= minimumMatches && stability.observedCells.every(cell => cell != null)
    ? stability.observedCells as number[]
    : null;
}

export function evaluateStability(
  history: CheckerRecognitionResult[],
  requiredMatches = REQUIRED_CELL_MATCHES,
  windowSize?: number,
  requiredLead = REQUIRED_CELL_LEAD
): StabilityResult {
  // By default the whole camera session contributes evidence. A cell that was
  // clearly visible earlier must not be forgotten while other rows are being read.
  const recent = windowSize == null ? history : history.slice(-windowSize);
  const candidateCells: Array<number | null> = []; const cellMatches: number[] = [];
  const cellStable: boolean[] = [];
  for (let cellIndex = 0; cellIndex < 6; cellIndex += 1) {
    const counts = new Map<string, { value: number; count: number; confidence: number; lastSeen: number }>();
    recent.forEach((result, resultIndex) => {
      const value = result.cells[cellIndex]; if (value == null) return;
      const key = value.toFixed(2); const current = counts.get(key) ?? { value, count: 0, confidence: 0, lastSeen: -1 };
      current.count += 1; current.confidence += result.confidence; current.lastSeen = resultIndex; counts.set(key, current);
    });
    const ranked = [...counts.values()].sort((a, b) => b.count - a.count || b.confidence - a.confidence || b.lastSeen - a.lastSeen);
    const best = ranked[0]; const runnerUp = ranked[1];
    candidateCells.push(best?.value ?? null); cellMatches.push(best?.count ?? 0);
    cellStable.push(Boolean(best && best.count >= requiredMatches && (!runnerUp || best.count - runnerUp.count >= requiredLead)));
  }
  if (candidateCells.every((value, index) => value != null && cellStable[index])) {
    return { state: "green", stableCells: candidateCells as number[], observedCells: candidateCells, matches: Math.min(...cellMatches) };
  }
  const current = recent.at(-1);
  const invalid = current?.warnings.some(warning => warning.code === "invalid_voltage" || warning.code === "poor_geometry");
  const partial = current?.lcdDetected && current.cells.some(value => value != null);
  return { state: !invalid && partial ? "yellow" : "red", stableCells: null, observedCells: candidateCells, matches: Math.min(...cellMatches) };
}
