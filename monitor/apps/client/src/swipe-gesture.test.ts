import { describe, expect, it } from "vitest";
import { classifyGesture, isCompletedBackSwipe, isCompletedHorizontalSwipe } from "./swipe-gesture";

describe("mobile swipe gestures", () => {
  it("keeps diagonal battery swipes horizontal in both directions", () => {
    expect(classifyGesture(90, 45)).toBe("horizontal");
    expect(classifyGesture(-90, 45)).toBe("horizontal");
    expect(isCompletedHorizontalSwipe(90, 45)).toBe(true);
    expect(isCompletedHorizontalSwipe(-90, 45)).toBe(true);
  });

  it("does not steal vertical list scrolling", () => {
    expect(classifyGesture(20, 80)).toBe("vertical");
    expect(isCompletedHorizontalSwipe(40, 100)).toBe(false);
  });

  it("uses only a left swipe for back navigation", () => {
    expect(isCompletedBackSwipe(-100, 20)).toBe(true);
    expect(isCompletedBackSwipe(100, 20)).toBe(false);
    expect(isCompletedBackSwipe(-60, 10)).toBe(false);
  });
});
