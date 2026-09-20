export function thresholdMask(gray: Uint8Array, threshold: number) {
  return Uint8Array.from(gray, value => value <= threshold ? 1 : 0);
}

export function adaptiveThresholdMask(gray: Uint8Array, width: number, height: number, radius = 16, offset = 11) {
  const integralWidth = width + 1; const integral = new Uint32Array(integralWidth * (height + 1));
  for (let y = 0; y < height; y += 1) {
    let rowSum = 0;
    for (let x = 0; x < width; x += 1) {
      rowSum += gray[y * width + x];
      integral[(y + 1) * integralWidth + x + 1] = integral[y * integralWidth + x + 1] + rowSum;
    }
  }
  const output = new Uint8Array(gray.length);
  for (let y = 0; y < height; y += 1) for (let x = 0; x < width; x += 1) {
    const left = Math.max(0, x - radius); const right = Math.min(width, x + radius + 1);
    const top = Math.max(0, y - radius); const bottom = Math.min(height, y + radius + 1);
    const sum = integral[bottom * integralWidth + right] - integral[top * integralWidth + right]
      - integral[bottom * integralWidth + left] + integral[top * integralWidth + left];
    const mean = sum / ((right - left) * (bottom - top));
    output[y * width + x] = gray[y * width + x] <= mean - offset ? 1 : 0;
  }
  return output;
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
