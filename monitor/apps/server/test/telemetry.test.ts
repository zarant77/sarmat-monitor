import { EventEmitter } from "node:events";
import { encode } from "@msgpack/msgpack";
import { describe, expect, it } from "vitest";
import type { WebSocket } from "ws";
import { TELEMETRY_STATUS, TelemetryHub, validateTelemetry } from "../src/telemetry.js";

class FakeSocket extends EventEmitter {
  terminated = false;
  closeCode: number | null = null;
  terminate() { this.terminated = true; this.emit("close"); }
  close(code: number) { this.closeCode = code; this.emit("close"); }
}

const crew = { id: "crew-a", groupId: "group-a", number: 1, name: "Alpha", color: "#FF0000" };

describe("telemetry protocol", () => {
  it("validates the MissionPlanner packet contract", () => {
    expect(validateTelemetry([7, 44.2, 18.5, 31, 0.5, 274, 123, -68, 3])).toBeNull();
    expect(validateTelemetry([7, 44.2])).toMatch(/9-element/);
    expect(validateTelemetry([7, 44.2, 18.5, 31, 0.5, 360, 123, -68, 3])).toMatch(/heading/);
  });

  it("creates scoped snapshots and marks disconnected data offline", () => {
    const hub = new TelemetryHub();
    const socket = new FakeSocket();
    hub.connect(socket as unknown as WebSocket, crew);
    socket.emit("message", encode([7, 44.2, 18.5, 31, 0.5, 274, 123, -68, 3]), true);
    const online = hub.snapshot([crew], Date.now())[0];
    expect(online.snapshot?.[0]).toBe(TELEMETRY_STATUS.ONLINE);
    expect(online.snapshot?.[3]).toBe(44.2);
    socket.emit("close");
    expect(hub.snapshot([crew], Date.now())[0].snapshot?.[0]).toBe(TELEMETRY_STATUS.OFFLINE);
  });

  it("replaces an older connection from the same crew", () => {
    const hub = new TelemetryHub();
    const first = new FakeSocket(); const second = new FakeSocket();
    hub.connect(first as unknown as WebSocket, crew);
    hub.connect(second as unknown as WebSocket, crew);
    expect(first.terminated).toBe(true);
  });
});
