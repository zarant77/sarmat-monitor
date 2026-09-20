import { describe, expect, it } from "vitest";
import { alignGrayscale, darkPercentileComposite } from "./temporal";

describe("temporal LCD image processing", () => {
  it("keeps a dark segment visible across flickering frames without trusting one outlier", () => {
    const frames = [
      Uint8Array.from([200, 200, 40, 200]), Uint8Array.from([200, 200, 42, 200]),
      Uint8Array.from([200, 200, 45, 200]), Uint8Array.from([200, 200, 200, 200]),
      Uint8Array.from([5, 200, 200, 200]), Uint8Array.from([200, 200, 200, 200])
    ];
    expect([...darkPercentileComposite(frames)]).toEqual([200, 200, 42, 200]);
  });

  it("aligns a slightly shifted frame to the reference", () => {
    const width = 20; const height = 20; const reference = new Uint8Array(width * height).fill(200);
    const shifted = new Uint8Array(width * height).fill(200);
    for (let y = 5; y < 15; y += 1) for (let x = 4; x < 7; x += 1) {
      reference[y * width + x] = 20; shifted[y * width + x + 2] = 20;
    }
    expect(alignGrayscale(reference, shifted, width, height, 3)).toEqual(reference);
  });
});
