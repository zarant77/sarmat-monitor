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
}

export interface CellVoltageLimits {
  min: number;
  max: number;
}

export type ScannerState = "red" | "yellow" | "green";

