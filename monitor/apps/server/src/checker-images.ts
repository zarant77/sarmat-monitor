export const MAX_CHECKER_IMAGE_BYTES = 4 * 1024 * 1024;
export const ALLOWED_CHECKER_IMAGE_TYPES = ["image/jpeg", "image/png", "image/webp"] as const;

export function validateCheckerImage(body: unknown, mimeType: string): Buffer {
  if (!Buffer.isBuffer(body) || body.length === 0) {
    throw Object.assign(new Error("Checker image is required"), { statusCode: 400 });
  }
  if (body.length > MAX_CHECKER_IMAGE_BYTES) {
    throw Object.assign(new Error("Checker image must not exceed 4 MB"), { statusCode: 413 });
  }
  if (!ALLOWED_CHECKER_IMAGE_TYPES.includes(mimeType as typeof ALLOWED_CHECKER_IMAGE_TYPES[number])) {
    throw Object.assign(new Error("Unsupported checker image type"), { statusCode: 415 });
  }

  const jpeg = body.length >= 3 && body[0] === 0xff && body[1] === 0xd8 && body[2] === 0xff;
  const png = body.length >= 8 && body.subarray(0, 8).equals(Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]));
  const webp = body.length >= 12 && body.subarray(0, 4).toString("ascii") === "RIFF" && body.subarray(8, 12).toString("ascii") === "WEBP";
  if (!(jpeg || png || webp)) {
    throw Object.assign(new Error("Checker image content does not match a supported image format"), { statusCode: 400 });
  }
  return body;
}
