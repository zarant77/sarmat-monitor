import { mergeNearby, regionDensity, runs } from "./masks";

const digitSegments: Record<number, string> = {
  0: "abcdef", 1: "bc", 2: "abdeg", 3: "abcdg", 4: "bcfg",
  5: "acdfg", 6: "acdefg", 7: "abc", 8: "abcdefg", 9: "abcdfg"
};

function readDigit(mask: Uint8Array, width: number, height: number) {
  if (width / height < .2) return { digit: 1, score: .9 };
  const densities: Record<string, number> = {
    a: regionDensity(mask, width, height, .20, .02, .80, .20), b: regionDensity(mask, width, height, .68, .10, .98, .48),
    c: regionDensity(mask, width, height, .68, .52, .98, .90), d: regionDensity(mask, width, height, .20, .80, .80, .98),
    e: regionDensity(mask, width, height, .02, .52, .32, .90), f: regionDensity(mask, width, height, .02, .10, .32, .48),
    g: regionDensity(mask, width, height, .20, .41, .80, .59)
  };
  let best = { digit: 0, loss: Number.POSITIVE_INFINITY };
  Object.entries(digitSegments).forEach(([digitText, active]) => {
    const loss = Object.entries(densities).reduce((sum, [segment, density]) => {
      const activation = Math.min(1, density / .25);
      return sum + (active.includes(segment) ? 1 - activation : activation) ** 2;
    }, 0) / 7;
    if (loss < best.loss) best = { digit: Number(digitText), loss };
  });
  return { digit: best.digit, score: Math.max(0, Math.min(1, 1 - best.loss)) };
}

export function readCellRows(lcdMask: Uint8Array, width: number, height: number) {
  const cells: Array<{ voltage: number | null; score: number }> = [];
  for (let rowIndex = 0; rowIndex < 6; rowIndex += 1) {
    const top = Math.round((rowIndex + .08) * height / 7); const bottom = Math.round((rowIndex + .92) * height / 7);
    const left = Math.round(width * .40); const right = Math.round(width * .98);
    const rowWidth = right - left; const rowHeight = bottom - top; const rowMask = new Uint8Array(rowWidth * rowHeight);
    for (let y = 0; y < rowHeight; y += 1) for (let x = 0; x < rowWidth; x += 1) rowMask[y * rowWidth + x] = lcdMask[(top + y) * width + left + x];
    const projection = Array.from({ length: rowWidth }, (_, x) => {
      let value = 0; for (let y = 0; y < rowHeight; y += 1) value += rowMask[y * rowWidth + x]; return value;
    });
    const candidates = mergeNearby(runs(projection, Math.max(2, Math.floor(rowHeight * .04))), Math.max(1, Math.floor(rowWidth * .01)))
      .filter(run => {
        let firstY = rowHeight; let lastY = -1;
        for (let y = 0; y < rowHeight; y += 1) for (let x = run.start; x <= run.end; x += 1) if (rowMask[y * rowWidth + x]) { firstY = Math.min(firstY, y); lastY = Math.max(lastY, y); }
        return lastY - firstY >= rowHeight * .35;
      }).sort((a, b) => b.weight - a.weight).slice(0, 3).sort((a, b) => a.start - b.start);
    const decimalPointDetected = candidates.length === 3 && runs(projection, 1).some(run => {
      let firstY = rowHeight; let lastY = -1;
      for (let y = 0; y < rowHeight; y += 1) for (let x = run.start; x <= run.end; x += 1) if (rowMask[y * rowWidth + x]) { firstY = Math.min(firstY, y); lastY = Math.max(lastY, y); }
      const height = lastY - firstY + 1;
      return run.start > candidates[0].end && run.end < candidates[1].start && height > 0 && height <= rowHeight * .3 && lastY >= rowHeight * .55;
    });
    const digits = candidates.map(candidate => {
      const digitWidth = candidate.end - candidate.start + 1; let digitTop = rowHeight; let digitBottom = -1;
      for (let y = 0; y < rowHeight; y += 1) for (let x = candidate.start; x <= candidate.end; x += 1) if (rowMask[y * rowWidth + x]) { digitTop = Math.min(digitTop, y); digitBottom = Math.max(digitBottom, y); }
      const digitHeight = Math.max(1, digitBottom - digitTop + 1); const digitMask = new Uint8Array(digitWidth * digitHeight);
      for (let y = 0; y < digitHeight; y += 1) for (let x = 0; x < digitWidth; x += 1) digitMask[y * digitWidth + x] = rowMask[(digitTop + y) * rowWidth + candidate.start + x];
      return readDigit(digitMask, digitWidth, digitHeight);
    });
    if (digits.length !== 3 || !decimalPointDetected || digits.some(digit => digit.score < .52)) cells.push({ voltage: null, score: digits.length ? Math.min(...digits.map(digit => digit.score)) : 0 });
    else cells.push({ voltage: Math.round((digits[0].digit + digits[1].digit / 10 + digits[2].digit / 100) * 100) / 100, score: Math.min(...digits.map(digit => digit.score)) });
  }
  return cells;
}
