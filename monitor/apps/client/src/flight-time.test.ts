import { describe, expect, it } from "vitest";
import { flightTimeToSeconds, formatFlightTime, splitFlightTime } from "./flight-time";

describe("flight time helpers", () => {
  it("converts whole hours and minutes to seconds", () => {
    expect(flightTimeToSeconds(127, 42)).toBe(459720);
  });

  it("rejects invalid values", () => {
    expect(() => flightTimeToSeconds(-1, 0)).toThrow();
    expect(() => flightTimeToSeconds(1, 60)).toThrow();
    expect(() => flightTimeToSeconds(1.5, 0)).toThrow();
  });

  it("splits and formats stored seconds", () => {
    expect(splitFlightTime(459779)).toEqual({ hours: 127, minutes: 42 });
    expect(formatFlightTime(459779, "год", "хв")).toBe("127 год 42 хв");
  });
});
