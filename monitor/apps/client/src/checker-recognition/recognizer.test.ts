import { describe, expect, it } from "vitest";
import { recognizeCheckerImage, recognitionFromCells } from "./recognizer";

const limits = { min: 3, max: 4.25 };

describe("browser checker recognizer", () => {
  it("returns the common partial result shape instead of guessing", () => {
    const image = { width: 360, height: 560, data: new Uint8ClampedArray(360 * 560 * 4).fill(200) };
    const result = recognizeCheckerImage(image, limits);
    expect(result).toEqual({ cells: Array(6).fill(null), confidence: 0, warnings: [{ code: "lcd_not_detected" }], lcdDetected: false, complete: false });
  });

  it("preserves partial cells and rejects physically invalid values", () => {
    const result = recognitionFromCells([
      { voltage: 4.18, score: .9 }, { voltage: null, score: .2 }, { voltage: 4.17, score: .8 },
      { voltage: 4.5, score: .9 }, { voltage: 4.18, score: .9 }, { voltage: 4.19, score: .9 }
    ], limits);
    expect(result.cells).toEqual([4.18, null, 4.17, null, 4.18, 4.19]);
    expect(result.complete).toBe(false);
    expect(result.warnings).toEqual(expect.arrayContaining([{ code: "unreadable_digit", cell: 2 }, { code: "invalid_voltage", cell: 4 }]));
  });
});
