export interface RecognitionWarning {
  code: "lcd_not_detected" | "poor_geometry" | "unreadable_digit" | "invalid_voltage";
  cell?: number;
}

export interface CheckerRecognitionResult {
  cells: Array<number | null>;
  confidence: number;
  warnings: RecognitionWarning[];
  lcdDetected: boolean;
  complete: boolean;
  lcdBounds?: NormalizedBounds;
  lcdQuad?: NormalizedPoint[];
}

export interface NormalizedPoint { x: number; y: number }
export interface NormalizedBounds extends NormalizedPoint { width: number; height: number }

export interface CellVoltageLimits {
  min: number;
  max: number;
}

export type ScannerState = "red" | "yellow" | "green";
