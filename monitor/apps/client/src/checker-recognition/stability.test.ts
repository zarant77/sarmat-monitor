import { describe, expect, it } from "vitest";
import { evaluateStability } from "./stability";
import type { CheckerRecognitionResult } from "./types";

const complete = (cells = [4.18, 4.19, 4.17, 4.19, 4.18, 4.18]): CheckerRecognitionResult => ({ cells, confidence: .9, warnings: [], lcdDetected: true, complete: true });
const partial: CheckerRecognitionResult = { cells: [4.18, null, null, null, null, null], confidence: .5, warnings: [{ code: "unreadable_digit", cell: 2 }], lcdDetected: true, complete: false };
const missing: CheckerRecognitionResult = { cells: Array(6).fill(null), confidence: 0, warnings: [{ code: "lcd_not_detected" }], lcdDetected: false, complete: false };

describe("scanner stability", () => {
  it("transitions red to yellow to green", () => {
    expect(evaluateStability([missing]).state).toBe("red");
    expect(evaluateStability([missing, partial]).state).toBe("yellow");
    expect(evaluateStability([partial, complete(), complete(), complete()])).toMatchObject({ state: "green", matches: 3, stableCells: complete().cells });
  });

  it("does not accept one isolated successful frame", () => {
    expect(evaluateStability([missing, complete(), partial, missing, partial])).toMatchObject({ state: "yellow", stableCells: null, matches: 1 });
  });
});
