import { describe, expect, it } from "vitest";
import { adaptiveThresholdMask } from "./masks";

describe("adaptive LCD threshold", () => {
  it("finds a dark segment across an uneven background", () => {
    const width = 60; const height = 30; const gray = new Uint8Array(width * height);
    for (let y = 0; y < height; y += 1) for (let x = 0; x < width; x += 1) {
      const background = 120 + Math.round(x / (width - 1) * 90);
      gray[y * width + x] = x >= 28 && x <= 32 && y >= 5 && y <= 24 ? background - 45 : background;
    }
    const mask = adaptiveThresholdMask(gray, width, height, 7, 10);
    let segmentInk = 0; let backgroundInk = 0;
    for (let y = 5; y <= 24; y += 1) {
      segmentInk += mask[y * width + 30]; backgroundInk += mask[y * width + 20];
    }
    expect(segmentInk).toBeGreaterThan(16);
    expect(backgroundInk).toBeLessThan(3);
  });
});
