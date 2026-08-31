import { describe, expect, it } from "vitest";
import { detectLcd, normalizeLcd } from "./lcd-detection";

function lcdImage(centerX: number, centerY: number, lcdWidth: number, lcdHeight: number, degrees: number) {
  const width = 540; const height = 800; const data = new Uint8ClampedArray(width * height * 4);
  const angle = degrees * Math.PI / 180; const cosine = Math.cos(angle); const sine = Math.sin(angle);
  for (let y = 0; y < height; y += 1) for (let x = 0; x < width; x += 1) {
    const relativeX = x - centerX; const relativeY = y - centerY;
    const localX = relativeX * cosine + relativeY * sine; const localY = -relativeX * sine + relativeY * cosine;
    const inside = Math.abs(localX) <= lcdWidth / 2 && Math.abs(localY) <= lcdHeight / 2;
    const value = inside ? 190 : 25; const index = (y * width + x) * 4;
    data[index] = value; data[index + 1] = value; data[index + 2] = value; data[index + 3] = 255;
  }
  return { data, width, height };
}

describe("LCD detection inside the search ROI", () => {
  it("finds a translated and mildly rotated display", () => {
    const image = lcdImage(315, 390, 250, 480, 8); const detected = detectLcd(image);
    expect(detected).not.toBeNull();
    expect(detected!.bounds.x).toBeGreaterThan(.25);
    expect(detected!.bounds.width).toBeGreaterThan(.4);
    expect(detected!.quad).toHaveLength(4);
    const normalized = normalizeLcd(image, detected!);
    expect(normalized).toMatchObject({ width: 340, height: 600 });
    expect(normalized.data[(300 * 340 + 170) * 4]).toBe(190);
  });

  it("rejects an LCD that is clipped by the search boundary", () => {
    expect(detectLcd(lcdImage(60, 400, 250, 480, 0))).toBeNull();
  });
});
