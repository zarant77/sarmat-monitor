export type GestureDirection = "undecided" | "horizontal" | "vertical";

export function classifyGesture(dx: number, dy: number, touchSlop = 12): GestureDirection {
  const absX = Math.abs(dx);
  const absY = Math.abs(dy);
  if (absX > touchSlop && absX >= absY * 0.7) return "horizontal";
  if (absY > touchSlop && absY > absX * 1.4) return "vertical";
  return "undecided";
}

export function isCompletedHorizontalSwipe(dx: number, dy: number, threshold = 88): boolean {
  return Math.abs(dx) > threshold && Math.abs(dx) > Math.abs(dy) * 1.2;
}

export function isCompletedBackSwipe(dx: number, dy: number, threshold = 88): boolean {
  return dx < -threshold && Math.abs(dx) > Math.abs(dy) * 1.2;
}
