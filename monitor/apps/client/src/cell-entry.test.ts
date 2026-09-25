import { describe, expect, it } from "vitest";
import { enterCells, isCompleteCell } from "./cell-entry";

const empty = () => Array<string>(12).fill("");
const enter = (cells: string[], index: number, text: string) => enterCells(cells, index, text, 3, 4.22);

describe("continuous cell voltage entry", () => {
  it("distributes the requested digit stream and moves to the next empty cell", () => {
    const result = enter(empty(), 0, "421420422");
    expect(result.cells.slice(0, 4)).toEqual(["4.21", "4.20", "4.22", ""]);
    expect(result.focusIndex).toBe(3);
  });
  it("supports uninterrupted individual key presses", () => {
    let cells = empty(); let index = 0;
    for (const digit of "421420422") {
      const result = enter(cells, index, cells[index] + digit);
      cells = result.cells; index = result.focusIndex ?? index;
    }
    expect(cells.slice(0, 3)).toEqual(["4.21", "4.20", "4.22"]);
    expect(index).toBe(3);
  });
  it("wraps from B to empty A and continues a pasted stream there", () => {
    const b = enter(empty(), 6, "421420422421420422");
    expect(b.focusIndex).toBe(0);
    expect(b.cells.slice(0, 6)).toEqual(Array(6).fill(""));
    const both = enter(empty(), 6, "421".repeat(12));
    expect(both.cells).toEqual(Array(12).fill("4.21"));
    expect(both.focusIndex).toBeNull();
  });
  it("skips existing cells and overwrites only the chosen starting cell", () => {
    const cells = empty(); cells[0] = "3.90"; cells[1] = "4.00"; cells[6] = "4.10";
    const result = enter(cells, 0, "421420");
    expect(result.cells.slice(0, 3)).toEqual(["4.21", "4.00", "4.20"]);
    expect(result.cells[6]).toBe("4.10");
    expect(enter(cells, 11, "421").focusIndex).toBe(2);
  });
  it("accepts comma, dot and separated pasted readings", () => {
    expect(enter(empty(), 0, "4,21 4.20\n4,22").cells.slice(0, 3)).toEqual(["4.21", "4.20", "4.22"]);
    expect(enter(empty(), 0, "421;420;422").cells.slice(0, 3)).toEqual(["4.21", "4.20", "4.22"]);
  });
  it("keeps partial entries unfinished and focuses their cell", () => {
    const result = enter(empty(), 0, "42142");
    expect(result.cells.slice(0, 2)).toEqual(["4.21", "42"]);
    expect(result.focusIndex).toBe(1);
    for (const value of ["", "4", "42", "4.", "4.2"]) expect(isCompleteCell(value, 3, 4.22)).toBe(false);
  });
  it("does not discard extra pasted values or overwrite populated cells", () => {
    const cells = Array(12).fill("4.00"); cells[0] = "";
    const result = enter(cells, 0, "421420");
    expect(result.error).toBe("overflow");
    expect(result.cells).toEqual(cells);
  });
  it("rejects invalid formats and does not advance on out-of-range voltage", () => {
    for (const text of ["-421", "4e2", "abc421", "4.211", "4,2,1"]) expect(enter(empty(), 0, text).error).toBe("format");
    const invalid = enter(empty(), 0, "499");
    expect(invalid.cells[0]).toBe("4.99");
    expect(invalid.focusIndex).toBeNull();
    expect(invalid.error).toBe("range");
    expect(enter(empty(), 0, "421499").cells).toEqual(empty());
  });
});
