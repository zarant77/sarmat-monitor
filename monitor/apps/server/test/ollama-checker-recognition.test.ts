import { describe, expect, it, vi } from "vitest";
import { mkdtemp, readFile, readdir, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { OllamaCheckerRecognizer } from "../src/ollama-checker-recognition.js";

const provider = { url: "http://localhost:11434", model: "qwen2.5vl:7b", timeoutMs: 5_000 };
const validation = { minCellVoltage: 2.5, maxCellVoltage: 4.5 };
const image = Buffer.from("checker-image");
const reply = (body: unknown, status = 200) => vi.fn(async () => new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } }));

describe("Ollama checker recognition", () => {
  it("sends the image with a strict structured schema and validates a successful result", async () => {
    const fetchFn = reply({ message: { content: JSON.stringify({ cells: [4.2, 4.19, 4.2, 4.2, 4.19, 4.2] }) } });
    const result = await new OllamaCheckerRecognizer(provider, validation, fetchFn).recognize(image, "A");

    expect(result).toMatchObject({ module: "A", complete: true });
    expect(result).not.toHaveProperty("totalVoltage");
    expect(result.cells.map(cell => cell.voltage)).toEqual([4.2, 4.19, 4.2, 4.2, 4.19, 4.2]);
    const [url, init] = fetchFn.mock.calls[0] as unknown as [string, RequestInit];
    const request = JSON.parse(String(init.body));
    expect(url).toBe("http://localhost:11434/api/chat");
    expect(request).toMatchObject({ model: "qwen2.5vl:7b", stream: false, options: { temperature: 0 }, format: "json" });
    expect(request.messages[0].images).toEqual([image.toString("base64")]);
    expect(request.messages[0].content).toContain("from top to bottom");
    expect(request.messages[0].content).toContain("Do not calculate or read Total");
    expect(request.messages[0].content).toContain('{"cells"');
  });

  it("can save the exact unmodified image payload sent to Ollama", async () => {
    const debugDir = await mkdtemp(join(tmpdir(), "sbm-ollama-debug-"));
    try {
      const fetchFn = reply({ message: { content: JSON.stringify({ cells: [4.2, 4.19, 4.2, 4.2, 4.19, 4.2] }) } });
      await new OllamaCheckerRecognizer({ ...provider, debugSaveImages: true, debugDir }, validation, fetchFn).recognize(image, "A");
      const directories = await readdir(debugDir);
      const files = await readdir(join(debugDir, directories[0]));
      const imageName = files.find(name => name.startsWith("ollama-input-A."));
      expect(imageName).toBeTruthy();
      expect(await readFile(join(debugDir, directories[0], imageName!))).toEqual(image);
    } finally {
      await rm(debugDir, { recursive: true, force: true });
    }
  });

  it("reports an unavailable Ollama service without crashing the server", async () => {
    const fetchFn = vi.fn(async () => { throw new TypeError("fetch failed"); });
    await expect(new OllamaCheckerRecognizer(provider, validation, fetchFn).recognize(image, "B")).rejects.toMatchObject({
      code: "CHECKER_RECOGNITION_FAILED",
      issues: [{ code: "provider_unavailable" }]
    });
  });

  it("reports a missing configured model clearly", async () => {
    const fetchFn = reply({ error: "model 'qwen2.5vl:7b' not found, try pulling it first" }, 404);
    await expect(new OllamaCheckerRecognizer(provider, validation, fetchFn).recognize(image, "A")).rejects.toMatchObject({
      issues: [{ code: "model_unavailable", message: "Ollama model 'qwen2.5vl:7b' is not installed" }]
    });
  });

  it("rejects malformed model JSON", async () => {
    const fetchFn = reply({ message: { content: "not json" } });
    await expect(new OllamaCheckerRecognizer(provider, validation, fetchFn).recognize(image, "A")).rejects.toMatchObject({ issues: [{ code: "invalid_model_response" }] });
  });

  it("rejects an incomplete result without guessing unreadable cells", async () => {
    const fetchFn = reply({ message: { content: JSON.stringify({ cells: [4.2, 4.19, null, 4.2, 4.19, 4.2] }) } });
    await expect(new OllamaCheckerRecognizer(provider, validation, fetchFn).recognize(image, "B")).rejects.toMatchObject({
      issues: [{ code: "unreadable_digit", field: "cells.2" }],
      partial: { module: "B", complete: false }
    });
  });

  it("rejects impossible cell values", async () => {
    const impossible = reply({ message: { content: JSON.stringify({ cells: [4.2, 4.19, 8.2, 4.2, 4.19, 4.2] }) } });
    await expect(new OllamaCheckerRecognizer(provider, validation, impossible).recognize(image, "A")).rejects.toMatchObject({ issues: expect.arrayContaining([expect.objectContaining({ code: "impossible_cell_voltage" })]) });
  });

  it("ignores an unsolicited checker Total returned by the model", async () => {
    const fetchFn = reply({ message: { content: JSON.stringify({ cells: Array(6).fill(4.2), totalVoltage: 99.99 }) } });
    const result = await new OllamaCheckerRecognizer(provider, validation, fetchFn).recognize(image, "A");
    expect(result.complete).toBe(true);
    expect(result).not.toHaveProperty("totalVoltage");
  });
});
