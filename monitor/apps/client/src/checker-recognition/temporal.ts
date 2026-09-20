export function alignGrayscale(reference: Uint8Array, frame: Uint8Array, width: number, height: number, maxShift = 3) {
  if (reference.length !== frame.length || frame.length !== width * height) return frame;
  let bestX = 0; let bestY = 0; let bestLoss = Number.POSITIVE_INFINITY;
  const left = Math.round(width * .03); const right = Math.round(width * .55);
  const top = Math.round(height * .02); const bottom = Math.round(height * .86);
  for (let shiftY = -maxShift; shiftY <= maxShift; shiftY += 1) for (let shiftX = -maxShift; shiftX <= maxShift; shiftX += 1) {
    let loss = 0; let samples = 0;
    for (let y = top + maxShift; y < bottom - maxShift; y += 2) for (let x = left + maxShift; x < right - maxShift; x += 2) {
      loss += Math.abs(reference[y * width + x] - frame[(y + shiftY) * width + x + shiftX]); samples += 1;
    }
    const movement = Math.abs(shiftX) + Math.abs(shiftY);
    const average = loss / Math.max(1, samples) + movement * .2;
    if (average < bestLoss) {
      bestLoss = average; bestX = shiftX; bestY = shiftY;
    }
  }
  if (bestX === 0 && bestY === 0) return frame;
  const aligned = new Uint8Array(frame.length);
  for (let y = 0; y < height; y += 1) for (let x = 0; x < width; x += 1) {
    const sourceX = x + bestX; const sourceY = y + bestY;
    aligned[y * width + x] = sourceX >= 0 && sourceX < width && sourceY >= 0 && sourceY < height
      ? frame[sourceY * width + sourceX]
      : reference[y * width + x];
  }
  return aligned;
}

export function darkPercentileComposite(frames: Uint8Array[], fraction = .2) {
  if (!frames.length) return new Uint8Array();
  const length = frames[0].length;
  if (frames.some(frame => frame.length !== length)) throw new Error("Temporal frames must have equal dimensions");
  const output = new Uint8Array(length); const rank = Math.min(2, Math.floor((frames.length - 1) * fraction));
  for (let index = 0; index < length; index += 1) {
    let first = 256; let second = 256; let third = 256;
    for (const frame of frames) {
      const value = frame[index];
      if (value < first) { third = second; second = first; first = value; }
      else if (value < second) { third = second; second = value; }
      else if (value < third) third = value;
    }
    output[index] = rank === 0 ? first : rank === 1 ? second : third;
  }
  return output;
}
