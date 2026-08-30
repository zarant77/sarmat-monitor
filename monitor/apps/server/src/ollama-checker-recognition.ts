import { randomUUID } from "node:crypto";
import { mkdir, writeFile } from "node:fs/promises";
import { join } from "node:path";
import type { CheckerModule, CheckerModuleRecognition } from "@sbm/shared";
import {
  CheckerRecognitionError,
  checkerRecognitionConfig,
  validateModuleRecognition,
  type CheckerRecognitionConfig,
  type CheckerRecognizer,
  type RecognitionIssue
} from "./checker-recognition.js";

type Fetch = typeof fetch;

export interface OllamaCheckerConfig {
  url: string;
  model: string;
  timeoutMs: number;
  debugSaveImages?: boolean;
  debugDir?: string;
  log?: boolean;
}

const envNumber = (name: string, fallback: number) => {
  const value = Number(process.env[name]);
  return Number.isFinite(value) && value > 0 ? value : fallback;
};

export const ollamaCheckerConfig = (): OllamaCheckerConfig => ({
  url: (process.env.OLLAMA_URL ?? "http://localhost:11434").replace(/\/$/, ""),
  model: process.env.OLLAMA_MODEL ?? "qwen2.5vl:7b",
  timeoutMs: envNumber("OLLAMA_TIMEOUT_MS", 120_000),
  debugSaveImages: process.env.OLLAMA_DEBUG_SAVE_IMAGES === "true",
  debugDir: process.env.OLLAMA_DEBUG_DIR ?? "build/ollama-debug",
  log: process.env.OLLAMA_RECOGNITION_LOG === "true" || process.env.NODE_ENV === "development"
});

const prompt = `You are an LCD transcription component. Read exactly six individual cell voltage values from the battery checker display.
Read the six rows from top to bottom. Preserve each displayed value exactly and treat every row independently.
Do not infer one row from another. Do not calculate or read Total. Ignore the checker Total display completely.
Do not guess unreadable digits. If a cell cannot be read reliably, return null in its position.
Return structured JSON only in this exact shape, with no other fields: {"cells":[4.20,4.19,4.20,4.20,4.19,4.20]}.`;

function imageExtension(image: Buffer) {
  if (image.length >= 8 && image.subarray(0, 8).equals(Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]))) return "png";
  if (image.length >= 12 && image.subarray(0, 4).toString("ascii") === "RIFF" && image.subarray(8, 12).toString("ascii") === "WEBP") return "webp";
  return "jpg";
}

function partialResult(module: CheckerModule, cells: Array<number | null>, issues: RecognitionIssue[]): CheckerModuleRecognition {
  return {
    module,
    cells: Array.from({ length: 6 }, (_, index) => ({ index: index + 1, voltage: cells[index] ?? null, confidence: cells[index] == null ? "low" : "high", score: cells[index] == null ? 0 : 1 })),
    confidence: cells.includes(null) ? 0 : 1,
    complete: issues.length === 0,
    issues: issues.map(({ code, field, message }) => ({ code, field, message }))
  };
}

export class OllamaCheckerRecognizer implements CheckerRecognizer {
  constructor(
    private readonly provider = ollamaCheckerConfig(),
    private readonly validation: CheckerRecognitionConfig = checkerRecognitionConfig,
    private readonly fetchFn: Fetch = fetch
  ) {}

  private log(event: string, details: Record<string, unknown>) {
    if (this.provider.log) console.info(`[checker-recognition] ${JSON.stringify({ event, ...details })}`);
  }

  private async saveExactPayload(image: Buffer, module: CheckerModule, requestId: string) {
    if (!this.provider.debugSaveImages) return null;
    const directory = join(this.provider.debugDir ?? "build/ollama-debug", requestId);
    const path = join(directory, `ollama-input-${module}.${imageExtension(image)}`);
    try {
      await mkdir(directory, { recursive: true });
      await writeFile(path, image);
      await writeFile(join(directory, "request.json"), JSON.stringify({ module, model: this.provider.model, bytes: image.length, prompt }, null, 2));
      return path;
    } catch (error) {
      this.log("debug_payload_save_failed", { module, requestId, error: error instanceof Error ? error.message : "unknown error" });
      return null;
    }
  }

  async recognize(image: Buffer, module: CheckerModule, limits: CheckerRecognitionConfig = this.validation): Promise<CheckerModuleRecognition> {
    const requestId = randomUUID();
    const startedAt = Date.now();
    const debugImagePath = await this.saveExactPayload(image, module, requestId);
    this.log("request_started", { requestId, module, model: this.provider.model, bytes: image.length, debugImagePath });
    let response: Response;
    try {
      response = await this.fetchFn(`${this.provider.url}/api/chat`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        signal: AbortSignal.timeout(this.provider.timeoutMs),
        body: JSON.stringify({
          model: this.provider.model,
          stream: false,
          format: "json",
          options: { temperature: 0 },
          messages: [{ role: "user", content: prompt, images: [image.toString("base64")] }]
        })
      });
    } catch (error) {
      const timedOut = error instanceof Error && (error.name === "TimeoutError" || error.name === "AbortError");
      this.log("request_failed", { requestId, module, durationMs: Date.now() - startedAt, reason: timedOut ? "timeout" : "provider_unavailable" });
      throw new CheckerRecognitionError([{ code: "provider_unavailable", message: timedOut ? "Ollama recognition timed out" : `Ollama is unavailable at ${this.provider.url}` }]);
    }

    const payload = await response.json().catch(() => null) as { message?: { content?: unknown }; error?: unknown } | null;
    if (!response.ok) {
      const providerMessage = typeof payload?.error === "string" ? payload.error : "Ollama request failed";
      const missing = response.status === 404 || /model.*(not found|missing)|pull model/i.test(providerMessage);
      this.log("request_failed", { requestId, module, durationMs: Date.now() - startedAt, status: response.status, reason: missing ? "model_unavailable" : "provider_error" });
      throw new CheckerRecognitionError([{ code: missing ? "model_unavailable" : "provider_unavailable", message: missing ? `Ollama model '${this.provider.model}' is not installed` : providerMessage }]);
    }

    if (typeof payload?.message?.content !== "string") {
      this.log("request_failed", { requestId, module, durationMs: Date.now() - startedAt, reason: "missing_structured_result" });
      throw new CheckerRecognitionError([{ code: "invalid_model_response", message: "Ollama returned no structured recognition result" }]);
    }

    let parsed: unknown;
    try { parsed = JSON.parse(payload.message.content); }
    catch {
      this.log("request_failed", { requestId, module, durationMs: Date.now() - startedAt, reason: "malformed_json" });
      throw new CheckerRecognitionError([{ code: "invalid_model_response", message: "Ollama returned malformed JSON" }]);
    }
    if (!parsed || typeof parsed !== "object") {
      this.log("request_failed", { requestId, module, durationMs: Date.now() - startedAt, reason: "invalid_result_shape" });
      throw new CheckerRecognitionError([{ code: "invalid_model_response", message: "Ollama returned an invalid recognition object" }]);
    }

    const raw = parsed as { cells?: unknown };
    const cells = Array.isArray(raw.cells) ? raw.cells.map(value => typeof value === "number" && Number.isFinite(value) ? value : null) : [];
    const issues: RecognitionIssue[] = [];
    if (!Array.isArray(raw.cells) || raw.cells.length !== 6) issues.push({ code: "invalid_cell_count", field: "cells", message: `Expected 6 cells, received ${Array.isArray(raw.cells) ? raw.cells.length : 0}` });
    cells.slice(0, 6).forEach((value, index) => {
      if (value == null) issues.push({ code: "unreadable_digit", field: `cells.${index}`, message: `Cell ${index + 1} could not be read reliably` });
    });

    const partial = partialResult(module, cells, issues);
    const incomplete = !Array.isArray(raw.cells) || raw.cells.length !== 6 || cells.some(value => value == null);
    if (incomplete) {
      this.log("request_failed", { requestId, module, durationMs: Date.now() - startedAt, reason: "incomplete_result", unreadableCells: cells.filter(value => value == null).length });
      throw new CheckerRecognitionError(issues, partial);
    }
    try {
      validateModuleRecognition({ module, cells: cells as number[] }, limits);
    } catch (error) {
      if (error instanceof CheckerRecognitionError) {
        this.log("request_failed", { requestId, module, durationMs: Date.now() - startedAt, reason: "physical_validation", issues: error.issues.map(issue => issue.code) });
        throw new CheckerRecognitionError(error.issues, partialResult(module, cells, error.issues));
      }
      throw error;
    }
    const result = partialResult(module, cells, []);
    this.log("request_succeeded", { requestId, module, durationMs: Date.now() - startedAt, cells });
    return result;
  }
}

export function createCheckerRecognizer(): CheckerRecognizer {
  return new OllamaCheckerRecognizer();
}
