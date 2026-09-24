import { describe, expect, it } from "vitest";
import { parseHistoryPagination } from "../src/history.js";

describe("battery history pagination", () => {
  it("uses safe defaults and bounds", () => {
    expect(parseHistoryPagination({})).toEqual({ offset: 0, limit: 25 });
    expect(parseHistoryPagination({ offset: "-10", limit: "500" })).toEqual({ offset: 0, limit: 50 });
    expect(parseHistoryPagination({ offset: "25", limit: "5" })).toEqual({ offset: 25, limit: 10 });
  });
});
