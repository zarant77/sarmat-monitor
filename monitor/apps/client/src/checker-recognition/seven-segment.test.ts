import { describe, expect, it } from "vitest";
import { readCellRows } from "./seven-segment";

const segments: Record<number, string> = {
  0: "abcdef", 1: "bc", 2: "abdeg", 3: "abcdg", 4: "bcfg",
  5: "acdfg", 6: "acdefg", 7: "abc", 8: "abcdefg", 9: "abcdfg"
};

function drawDigit(mask: Uint8Array, canvasWidth: number, x: number, y: number, digit: number) {
  const width = 34; const height = 56; const thickness = 6;
  const rectangles: Record<string, [number, number, number, number]> = {
    a: [thickness, 0, width - thickness * 2, thickness], b: [width - thickness, thickness, thickness, height / 2 - thickness],
    c: [width - thickness, height / 2, thickness, height / 2 - thickness], d: [thickness, height - thickness, width - thickness * 2, thickness],
    e: [0, height / 2, thickness, height / 2 - thickness], f: [0, thickness, thickness, height / 2 - thickness],
    g: [thickness, height / 2 - thickness / 2, width - thickness * 2, thickness]
  };
  for (const segment of segments[digit]) {
    const [left, top, segmentWidth, segmentHeight] = rectangles[segment];
    for (let row = top; row < top + segmentHeight; row += 1) for (let column = left; column < left + segmentWidth; column += 1) mask[(y + row) * canvasWidth + x + column] = 1;
  }
  if (digit === 1) for (let offset = 0; offset < 9; offset += 1) mask[(y + 5 + offset) * canvasWidth + x + width - thickness - offset] = 1;
}

function checkerMask(includeFirstDecimal = true) {
  const width = 340; const height = 600; const mask = new Uint8Array(width * height);
  for (let row = 0; row < 6; row += 1) {
    // Mirrors the photographed checker: lower rows drift upward relative to a
    // simplistic seven-equal-bands layout, so row locations must be detected.
    const top = Math.round((.07 + row * .136) * height - 28);
    drawDigit(mask, width, 12, top, 4); drawDigit(mask, width, 62, top, row === 1 ? 1 : 2); drawDigit(mask, width, 112, top, row === 1 ? 8 : 0);
    if (row > 0 || includeFirstDecimal) for (let y = top + 46; y < top + 52; y += 1) for (let x = 53; x < 58; x += 1) mask[y * width + x] = 1;
  }
  return { mask, width, height };
}

describe("seven-segment row layout", () => {
  it("reads the six left-side voltage rows and ignores right-side LCD icons", () => {
    const { mask, width, height } = checkerMask();
    const cells = readCellRows(mask, width, height);
    expect(cells.map(cell => cell.voltage)).toEqual([4.2, 4.18, 4.2, 4.2, 4.2, 4.2]);
  });

  it("ignores a missing decimal point when all three fixed-layout digits are clear", () => {
    const { mask, width, height } = checkerMask(false); const cells = readCellRows(mask, width, height);
    expect(cells[0].voltage).toBe(4.2);
    expect(cells[0].score).toBe(cells[2].score);
  });
});
