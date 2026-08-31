export interface BrowserImageData {
  readonly data: Uint8ClampedArray;
  readonly width: number;
  readonly height: number;
}

export function grayscale(image: BrowserImageData): Uint8Array {
  const output = new Uint8Array(image.width * image.height);
  for (let source = 0, target = 0; target < output.length; source += 4, target += 1) {
    output[target] = Math.round(image.data[source] * .299 + image.data[source + 1] * .587 + image.data[source + 2] * .114);
  }
  return output;
}

export function cropGray(source: Uint8Array, sourceWidth: number, left: number, top: number, width: number, height: number) {
  const output = new Uint8Array(width * height);
  for (let y = 0; y < height; y += 1) {
    output.set(source.subarray((top + y) * sourceWidth + left, (top + y) * sourceWidth + left + width), y * width);
  }
  return output;
}

export function percentile(values: Uint8Array, fraction: number) {
  const histogram = new Uint32Array(256);
  values.forEach(value => { histogram[value] += 1; });
  const target = values.length * fraction;
  let total = 0;
  for (let value = 0; value < histogram.length; value += 1) {
    total += histogram[value];
    if (total >= target) return value;
  }
  return 255;
}

