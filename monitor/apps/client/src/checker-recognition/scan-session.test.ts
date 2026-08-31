import { describe, expect, it } from "vitest";
import { clearModuleScan, combinedScans, correctCombinedCell, setModuleScan } from "./scan-session";

describe("Battery A/B scan session", () => {
  it("keeps modules independent and combines them in A then B order", () => {
    const a = [4.18, 4.19, 4.17, 4.19, 4.18, 4.18]; const b = [4.17, 4.18, 4.18, 4.19, 4.18, 4.17];
    const onlyA = setModuleScan({}, "A", a);
    expect(combinedScans(onlyA)).toBeNull();
    expect(combinedScans(setModuleScan(onlyA, "B", b))).toEqual([...a, ...b]);
  });

  it("supports retry and operator correction", () => {
    const state = setModuleScan(setModuleScan({}, "A", Array(6).fill(4.1)), "B", Array(6).fill(4.2));
    expect(clearModuleScan(state, "A").B).toEqual(Array(6).fill(4.2));
    expect(correctCombinedCell(combinedScans(state)!, 0, 4.15)[0]).toBe(4.15);
  });
});
