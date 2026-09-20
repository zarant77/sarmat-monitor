import { describe, expect, it } from "vitest";
import { cellsReadyForReview, evaluateStability } from "./stability";
import type { CheckerRecognitionResult } from "./types";

const complete = (cells = [4.18, 4.19, 4.17, 4.19, 4.18, 4.18]): CheckerRecognitionResult => ({ cells, confidence: .9, warnings: [], lcdDetected: true, complete: true });
const partial: CheckerRecognitionResult = { cells: [4.18, null, null, null, null, null], confidence: .5, warnings: [{ code: "unreadable_digit", cell: 2 }], lcdDetected: true, complete: false };
const missing: CheckerRecognitionResult = { cells: Array(6).fill(null), confidence: 0, warnings: [{ code: "lcd_not_detected" }], lcdDetected: false, complete: false };

describe("scanner stability", () => {
  it("transitions red to yellow to green", () => {
    expect(evaluateStability([missing]).state).toBe("red");
    expect(evaluateStability([missing, partial]).state).toBe("yellow");
    expect(evaluateStability([partial, complete(), complete(), complete(), complete()])).toMatchObject({ state: "green", matches: 4, stableCells: complete().cells });
  });

  it("does not accept one isolated successful frame", () => {
    expect(evaluateStability([missing, complete(), partial, missing, partial])).toMatchObject({ state: "yellow", stableCells: null });
  });

  it("stabilizes independently blinking rows without requiring one perfect frame", () => {
    const values = [3.4, 3.35, 3.41, 3.38, 3.34, 3.37];
    const frame = (visible: boolean[]): CheckerRecognitionResult => ({
      cells: values.map((value, index) => visible[index] ? value : null), confidence: .8,
      warnings: [], lcdDetected: true, complete: visible.every(Boolean)
    });
    const history = [
      frame([true, false, true, false, true, true]), frame([true, true, false, true, true, false]),
      frame([true, true, true, true, false, true]), frame([false, true, true, true, true, true]),
      frame([true, false, true, true, true, true]), frame([true, true, true, false, true, true])
    ];
    expect(history.some(result => result.complete)).toBe(false);
    expect(evaluateStability(history)).toMatchObject({ state: "green", matches: 4, stableCells: values });
  });

  it("keeps early cell evidence while later rows become readable", () => {
    const values = [4.18, 4.21, 4.18, 4.2, 4.2, 4.19];
    const frame = (visible: boolean[]): CheckerRecognitionResult => ({
      cells: values.map((value, index) => visible[index] ? value : null), confidence: .85,
      warnings: [], lcdDetected: true, complete: visible.every(Boolean)
    });
    const history = [
      ...Array.from({ length: 4 }, () => frame([true, false, false, false, false, false])),
      ...Array.from({ length: 8 }, () => missing),
      ...Array.from({ length: 4 }, () => frame([false, true, true, true, true, true]))
    ];
    expect(evaluateStability(history)).toMatchObject({ state: "green", stableCells: values });
  });

  it("chooses the most frequent value for each cell", () => {
    const expected = [4.18, 4.21, 4.18, 4.2, 4.2, 4.19];
    const wrong = [4.14, 4.17, 4.13, 4.16, 4.15, 4.13];
    const history = [
      ...Array.from({ length: 3 }, () => complete(wrong)),
      ...Array.from({ length: 6 }, () => complete(expected))
    ];
    expect(evaluateStability(history)).toMatchObject({ state: "green", matches: 6, stableCells: expected });
  });

  it("waits when two values have almost equal support", () => {
    const expected = [4.18, 4.21, 4.18, 4.2, 4.2, 4.19];
    const disputed = [4.14, ...expected.slice(1)];
    const history = [
      ...Array.from({ length: 3 }, () => complete(disputed)),
      ...Array.from({ length: 4 }, () => complete(expected))
    ];
    expect(evaluateStability(history)).toMatchObject({ state: "yellow", stableCells: null });
  });

  it("offers a review after every cell has repeated even without a strong consensus", () => {
    const expected = [4.18, 4.16, 4.14, 4.16, 4.18, 4.16];
    const disputed = [...expected.slice(0, 5), 4.08];
    const history = [
      ...Array.from({ length: 2 }, () => complete(expected)),
      ...Array.from({ length: 2 }, () => complete(disputed)),
      ...Array.from({ length: 8 }, () => missing)
    ];
    const stability = evaluateStability(history);
    expect(stability.stableCells).toBeNull();
    expect(cellsReadyForReview(stability, history.length)).toEqual(disputed);
  });

  it("does not offer an early or single-frame candidate for review", () => {
    const values = [4.18, 4.16, 4.14, 4.16, 4.18, 4.16];
    expect(cellsReadyForReview(evaluateStability([complete(values)]), 1)).toBeNull();
    expect(cellsReadyForReview(evaluateStability(Array.from({ length: 12 }, () => partial)), 12)).toBeNull();
  });
});
