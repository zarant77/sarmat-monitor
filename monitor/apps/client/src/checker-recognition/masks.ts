export function thresholdMask(gray: Uint8Array, threshold: number) {
  return Uint8Array.from(gray, value => value <= threshold ? 1 : 0);
}

export function regionDensity(mask: Uint8Array, width: number, height: number, left: number, top: number, right: number, bottom: number) {
  const x0 = Math.max(0, Math.floor(left * width)); const x1 = Math.min(width, Math.ceil(right * width));
  const y0 = Math.max(0, Math.floor(top * height)); const y1 = Math.min(height, Math.ceil(bottom * height));
  let ink = 0;
  for (let y = y0; y < y1; y += 1) for (let x = x0; x < x1; x += 1) ink += mask[y * width + x];
  return ink / Math.max(1, (x1 - x0) * (y1 - y0));
}

export function runs(values: number[], minimum: number) {
  const found: Array<{ start: number; end: number; weight: number }> = [];
  let start = -1;
  for (let index = 0; index <= values.length; index += 1) {
    if (index < values.length && values[index] >= minimum) { if (start < 0) start = index; }
    else if (start >= 0) {
      found.push({ start, end: index - 1, weight: values.slice(start, index).reduce((sum, value) => sum + value, 0) });
      start = -1;
    }
  }
  return found;
}

export function mergeNearby(input: ReturnType<typeof runs>, gap: number) {
  const merged: ReturnType<typeof runs> = [];
  input.forEach(current => {
    const previous = merged.at(-1);
    if (previous && current.start - previous.end <= gap) { previous.end = current.end; previous.weight += current.weight; }
    else merged.push({ ...current });
  });
  return merged;
}

