import { grayscale, percentile, type BrowserImageData } from "./image-utils";
import { thresholdMask } from "./masks";
import { readCellRows } from "./seven-segment";
import type { CellVoltageLimits, CheckerRecognitionResult, RecognitionWarning } from "./types";

export function recognitionFromCells(readings: Array<{ voltage: number | null; score: number }>, limits: CellVoltageLimits, lcdDetected = true): CheckerRecognitionResult {
  const warnings: RecognitionWarning[] = [];
  const cells = Array.from({ length: 6 }, (_, index) => {
    const reading = readings[index] ?? { voltage: null, score: 0 };
    if (reading.voltage == null || reading.score < .52) { warnings.push({ code: "unreadable_digit", cell: index + 1 }); return null; }
    if (!Number.isFinite(reading.voltage) || reading.voltage < limits.min || reading.voltage > limits.max) { warnings.push({ code: "invalid_voltage", cell: index + 1 }); return null; }
    return reading.voltage;
  });
  const scores = readings.filter(reading => reading.voltage != null).map(reading => reading.score);
  return { cells, confidence: scores.length ? Math.round(scores.reduce((sum, score) => sum + score, 0) / scores.length * 1000) / 1000 : 0, warnings, lcdDetected, complete: cells.every(value => value != null) && warnings.length === 0 };
}

export function recognizeCheckerImage(image: BrowserImageData, limits: CellVoltageLimits): CheckerRecognitionResult {
  if (image.width < 180 || image.height < 280 || image.width / image.height < .48 || image.width / image.height > .68) {
    return { cells: Array(6).fill(null), confidence: 0, warnings: [{ code: "poor_geometry" }], lcdDetected: false, complete: false };
  }
  const lcd = grayscale(image); const width = image.width; const height = image.height;
  const low = percentile(lcd, .08); const high = percentile(lcd, .88);
  const threshold = Math.min(155, Math.max(45, percentile(lcd, .22)));
  const mask = thresholdMask(lcd, threshold);
  const inkRatio = mask.reduce((sum, value) => sum + value, 0) / mask.length;
  const lcdDetected = high - low >= 28 && inkRatio >= .01 && inkRatio <= .45;
  if (!lcdDetected) return { cells: Array(6).fill(null), confidence: 0, warnings: [{ code: "lcd_not_detected" }], lcdDetected: false, complete: false };
  return recognitionFromCells(readCellRows(mask, width, height), limits, true);
}
