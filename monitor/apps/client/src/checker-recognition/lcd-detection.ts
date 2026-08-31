import { grayscale, percentile, type BrowserImageData } from "./image-utils";
import type { NormalizedBounds, NormalizedPoint } from "./types";

interface Point { x: number; y: number }

export interface DetectedLcd {
  bounds: NormalizedBounds;
  quad: NormalizedPoint[];
  score: number;
}

const clamp01 = (value: number) => Math.max(0, Math.min(1, value));

export function detectLcd(image: BrowserImageData): DetectedLcd | null {
  const step = image.width >= 500 ? 2 : 1;
  const width = Math.floor(image.width / step); const height = Math.floor(image.height / step);
  const fullGray = grayscale(image); const gray = new Uint8Array(width * height);
  for (let y = 0; y < height; y += 1) for (let x = 0; x < width; x += 1) gray[y * width + x] = fullGray[y * step * image.width + x * step];
  const dark = percentile(gray, .2); const light = percentile(gray, .82);
  if (light - dark < 24) return null;
  const threshold = Math.round(dark + (light - dark) * .52);
  const active = Uint8Array.from(gray, value => value >= threshold ? 1 : 0);
  const visited = new Uint8Array(active.length); const queue = new Int32Array(active.length);
  let best: DetectedLcd | null = null;

  for (let seed = 0; seed < active.length; seed += 1) {
    if (!active[seed] || visited[seed]) continue;
    let head = 0; let tail = 0; queue[tail++] = seed; visited[seed] = 1;
    let minX = width; let maxX = 0; let minY = height; let maxY = 0; let sumX = 0; let sumY = 0;
    while (head < tail) {
      const index = queue[head++]; const x = index % width; const y = Math.floor(index / width);
      minX = Math.min(minX, x); maxX = Math.max(maxX, x); minY = Math.min(minY, y); maxY = Math.max(maxY, y); sumX += x; sumY += y;
      let next: number;
      if (x > 0) { next = index - 1; if (!visited[next] && active[next]) { visited[next] = 1; queue[tail++] = next; } }
      if (x + 1 < width) { next = index + 1; if (!visited[next] && active[next]) { visited[next] = 1; queue[tail++] = next; } }
      if (y > 0) { next = index - width; if (!visited[next] && active[next]) { visited[next] = 1; queue[tail++] = next; } }
      if (y + 1 < height) { next = index + width; if (!visited[next] && active[next]) { visited[next] = 1; queue[tail++] = next; } }
    }
    const boxWidth = maxX - minX + 1; const boxHeight = maxY - minY + 1; const boxArea = boxWidth * boxHeight;
    if (tail < boxArea * .42 || boxWidth < width * .2 || boxHeight < height * .24 || boxWidth > width * .94 || boxHeight > height * .94) continue;
    if (minX <= 2 || minY <= 2 || maxX >= width - 3 || maxY >= height - 3) continue;

    const centerX = sumX / tail; const centerY = sumY / tail;
    let covarianceX = 0; let covarianceY = 0; let covarianceXY = 0;
    for (let index = 0; index < tail; index += 1) {
      const x = queue[index] % width - centerX; const y = Math.floor(queue[index] / width) - centerY;
      covarianceX += x * x; covarianceY += y * y; covarianceXY += x * y;
    }
    const trace = covarianceX + covarianceY; const delta = Math.sqrt((covarianceX - covarianceY) ** 2 + 4 * covarianceXY ** 2);
    const lambda = (trace + delta) / 2;
    let verticalX = covarianceXY; let verticalY = lambda - covarianceX;
    if (Math.abs(verticalX) + Math.abs(verticalY) < .001) { verticalX = 0; verticalY = 1; }
    const vectorLength = Math.hypot(verticalX, verticalY); verticalX /= vectorLength; verticalY /= vectorLength;
    if (verticalY < 0) { verticalX *= -1; verticalY *= -1; }
    if (Math.abs(verticalY) < .72) continue;
    const horizontalX = verticalY; const horizontalY = -verticalX;
    let minHorizontal = Infinity; let maxHorizontal = -Infinity; let minVertical = Infinity; let maxVertical = -Infinity;
    for (let index = 0; index < tail; index += 1) {
      const x = queue[index] % width - centerX; const y = Math.floor(queue[index] / width) - centerY;
      const horizontal = x * horizontalX + y * horizontalY; const vertical = x * verticalX + y * verticalY;
      minHorizontal = Math.min(minHorizontal, horizontal); maxHorizontal = Math.max(maxHorizontal, horizontal);
      minVertical = Math.min(minVertical, vertical); maxVertical = Math.max(maxVertical, vertical);
    }
    const orientedWidth = maxHorizontal - minHorizontal; const orientedHeight = maxVertical - minVertical; const aspect = orientedWidth / orientedHeight;
    if (aspect < .42 || aspect > .82 || orientedHeight < height * .24) continue;
    const point = (horizontal: number, vertical: number): Point => ({
      x: (centerX + horizontalX * horizontal + verticalX * vertical) * step,
      y: (centerY + horizontalY * horizontal + verticalY * vertical) * step
    });
    const points = [point(minHorizontal, minVertical), point(maxHorizontal, minVertical), point(maxHorizontal, maxVertical), point(minHorizontal, maxVertical)];
    const xs = points.map(item => item.x); const ys = points.map(item => item.y);
    const bounds = {
      x: clamp01(Math.min(...xs) / image.width), y: clamp01(Math.min(...ys) / image.height),
      width: clamp01((Math.max(...xs) - Math.min(...xs)) / image.width), height: clamp01((Math.max(...ys) - Math.min(...ys)) / image.height)
    };
    const fill = tail / Math.max(1, orientedWidth * orientedHeight); const area = orientedWidth * orientedHeight / (width * height);
    const score = fill + Math.min(.5, area) + (light - dark) / 255;
    if (!best || score > best.score) best = { bounds, quad: points.map(item => ({ x: clamp01(item.x / image.width), y: clamp01(item.y / image.height) })), score };
  }
  return best;
}

export function normalizeLcd(image: BrowserImageData, detected: DetectedLcd, width = 340, height = 600): BrowserImageData {
  const [topLeft, topRight, bottomRight, bottomLeft] = detected.quad.map(point => ({ x: point.x * image.width, y: point.y * image.height }));
  const output = new Uint8ClampedArray(width * height * 4);
  for (let y = 0; y < height; y += 1) {
    const vertical = (y + .5) / height;
    for (let x = 0; x < width; x += 1) {
      const horizontal = (x + .5) / width;
      const leftX = topLeft.x + (bottomLeft.x - topLeft.x) * vertical; const leftY = topLeft.y + (bottomLeft.y - topLeft.y) * vertical;
      const rightX = topRight.x + (bottomRight.x - topRight.x) * vertical; const rightY = topRight.y + (bottomRight.y - topRight.y) * vertical;
      const sourceX = Math.max(0, Math.min(image.width - 1, Math.round(leftX + (rightX - leftX) * horizontal)));
      const sourceY = Math.max(0, Math.min(image.height - 1, Math.round(leftY + (rightY - leftY) * horizontal)));
      const source = (sourceY * image.width + sourceX) * 4; const target = (y * width + x) * 4;
      output[target] = image.data[source]; output[target + 1] = image.data[source + 1]; output[target + 2] = image.data[source + 2]; output[target + 3] = image.data[source + 3];
    }
  }
  return { data: output, width, height };
}
