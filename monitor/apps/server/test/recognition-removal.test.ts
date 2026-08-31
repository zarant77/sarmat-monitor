import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";

describe("server recognition removal", () => {
  it("has no model/CV dependency or image-recognition route", () => {
    const packageJson = readFileSync(new URL("../package.json", import.meta.url), "utf8");
    const appSource = readFileSync(new URL("../src/app.ts", import.meta.url), "utf8");
    expect(packageJson).not.toMatch(/sharp|ollama|opencv/i);
    expect(appSource).not.toMatch(/checker-images|recognizer|image\/(jpeg|png|webp)|addContentTypeParser/i);
    expect(appSource).toContain("/api/batteries/:id/measurement-preview");
  });
});
