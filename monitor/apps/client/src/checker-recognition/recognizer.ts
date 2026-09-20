import { grayscale, percentile, type BrowserImageData } from "./image-utils";
import { adaptiveThresholdMask, thresholdMask } from "./masks";
import { readCellRows } from "./seven-segment";
import { detectLcd, normalizeLcd, type DetectedLcd } from "./lcd-detection";
import { alignGrayscale, darkPercentileComposite } from "./temporal";
import type { CellVoltageLimits, CheckerRecognitionResult, RecognitionWarning } from "./types";

export interface RecognitionDebugData { detected: DetectedLcd | null; normalizedLcd?: BrowserImageData; result: CheckerRecognitionResult }
export interface RecognitionOptions { onDebug?: (data: RecognitionDebugData) => void }
type CellReading = { voltage: number | null; score: number };

const emptyResult = (warning: RecognitionWarning["code"], lcdDetected = false): CheckerRecognitionResult => ({
  cells: Array(6).fill(null), confidence: 0, warnings: [{ code: warning }], lcdDetected, complete: false
});

const geometryValid = (image: BrowserImageData) => image.width >= 240 && image.height >= 360 && image.width / image.height >= .58 && image.width / image.height <= .76;

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

function combineThresholdReadings(variants: CellReading[][], limits: CellVoltageLimits): CellReading[] {
  return Array.from({ length: 6 }, (_, cellIndex) => {
    const candidates = new Map<string, { voltage: number; count: number; score: number }>();
    variants.forEach(readings => {
      const reading = readings[cellIndex];
      if (!reading || reading.voltage == null || reading.voltage < limits.min || reading.voltage > limits.max) return;
      const key = reading.voltage.toFixed(2); const candidate = candidates.get(key) ?? { voltage: reading.voltage, count: 0, score: 0 };
      candidate.count += 1; candidate.score += reading.score; candidates.set(key, candidate);
    });
    const ranked = [...candidates.values()].sort((a, b) => b.count - a.count || b.score - a.score);
    const best = ranked[0]; const runnerUp = ranked[1];
    if (!best) return { voltage: null, score: 0 };
    let score = best.score / best.count;
    if (best.count === 1 && variants.length > 1) score *= .62;
    if (runnerUp?.count === best.count) score *= .7;
    else if (runnerUp && best.count - runnerUp.count === 1) score *= .9;
    return { voltage: best.voltage, score };
  });
}

function recognizeNormalizedGray(lcd: Uint8Array, width: number, height: number, limits: CellVoltageLimits) {
  const low = percentile(lcd, .08); const high = percentile(lcd, .88);
  if (high - low < 28) return emptyResult("unreadable_digit", true);
  const thresholds = [...new Set([.18, .22, .26].map(fraction => Math.min(165, Math.max(45, percentile(lcd, fraction)))))];
  const masks = thresholds.map(threshold => thresholdMask(lcd, threshold));
  masks.push(adaptiveThresholdMask(lcd, width, height));
  const usableMasks = masks.filter(mask => {
    const inkRatio = mask.reduce((sum, value) => sum + value, 0) / mask.length;
    return inkRatio >= .01 && inkRatio <= .45;
  });
  if (!usableMasks.length) return emptyResult("unreadable_digit", true);
  return recognitionFromCells(combineThresholdReadings(usableMasks.map(mask => readCellRows(mask, width, height)), limits), limits, true);
}

function grayscaleImageData(gray: Uint8Array, width: number, height: number): BrowserImageData {
  const data = new Uint8ClampedArray(gray.length * 4);
  gray.forEach((value, index) => { const target = index * 4; data[target] = value; data[target + 1] = value; data[target + 2] = value; data[target + 3] = 255; });
  return { data, width, height };
}

export function recognizeCheckerImage(image: BrowserImageData, limits: CellVoltageLimits, options: RecognitionOptions = {}): CheckerRecognitionResult {
  if (!geometryValid(image)) {
    const result = emptyResult("poor_geometry");
    options.onDebug?.({ detected: null, result }); return result;
  }
  const detected = detectLcd(image);
  if (!detected) {
    const result = emptyResult("lcd_not_detected");
    options.onDebug?.({ detected: null, result }); return result;
  }
  const normalizedLcd = normalizeLcd(image, detected); const result = {
    ...recognizeNormalizedGray(grayscale(normalizedLcd), normalizedLcd.width, normalizedLcd.height, limits),
    lcdBounds: detected.bounds, lcdQuad: detected.quad
  };
  options.onDebug?.({ detected, normalizedLcd, result }); return result;
}

export class TemporalCheckerRecognizer {
  private frames: Uint8Array[] = [];

  constructor(private readonly maximumFrames = 12) {}

  reset() { this.frames = []; }

  recognize(image: BrowserImageData, limits: CellVoltageLimits, options: RecognitionOptions = {}): CheckerRecognitionResult {
    if (!geometryValid(image)) {
      const result = emptyResult("poor_geometry"); options.onDebug?.({ detected: null, result }); return result;
    }
    const detected = detectLcd(image);
    if (!detected) {
      const result = emptyResult("lcd_not_detected"); options.onDebug?.({ detected: null, result }); return result;
    }
    const normalized = normalizeLcd(image, detected); let gray = grayscale(normalized);
    const low = percentile(gray, .08); const high = percentile(gray, .88);
    if (high - low >= 28) {
      const reference = this.frames.at(-1);
      if (reference) gray = alignGrayscale(reference, gray, normalized.width, normalized.height);
      this.frames.push(gray); if (this.frames.length > this.maximumFrames) this.frames.shift();
    }
    const composite = this.frames.length ? darkPercentileComposite(this.frames) : gray;
    const normalizedLcd = grayscaleImageData(composite, normalized.width, normalized.height);
    const result = {
      ...recognizeNormalizedGray(composite, normalized.width, normalized.height, limits),
      lcdBounds: detected.bounds, lcdQuad: detected.quad
    };
    options.onDebug?.({ detected, normalizedLcd, result }); return result;
  }
}
