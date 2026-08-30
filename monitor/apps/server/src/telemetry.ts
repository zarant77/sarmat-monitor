import { decode } from "@msgpack/msgpack";
import type { WebSocket } from "ws";

export const TELEMETRY_STATUS = Object.freeze({ ONLINE: 0, STALE: 1, OFFLINE: 2 });
export const TELEMETRY_THRESHOLDS = Object.freeze({
  voltage: { goodMin: 44, normalMin: 42 },
  current: { goodMax: 80, normalMax: 120 },
  satellites: { goodMin: 30, normalMin: 26 },
  hdop: { goodMax: 0.6, normalMax: 0.8 },
  linkRssi: { goodMin: -70, normalMin: -80 }
});

export type TelemetryPacket = [number, number | null, number | null, number | null, number | null, number | null, number | null, number | null, number];
export interface TelemetryCrew { id: string; groupId: string; number: number; name: string; color: string; }
interface TelemetryState { socket: WebSocket | null; connected: boolean; telemetry: TelemetryPacket | null; receivedAt: number | null; }

function nullableNumber(value: unknown): value is number | null {
  return value === null || (typeof value === "number" && Number.isFinite(value));
}
function nullableInteger(value: unknown, minimum: number, maximum: number): boolean {
  return value === null || (Number.isInteger(value) && (value as number) >= minimum && (value as number) <= maximum);
}

export function validateTelemetry(packet: unknown): string | null {
  if (!Array.isArray(packet) || packet.length !== 9) return "telemetry must be a 9-element MessagePack array";
  const [sequence, voltage, current, satellites, hdop, heading, altitude, linkRssi, flags] = packet;
  if (!Number.isInteger(sequence) || sequence < 0 || sequence > 0xffffffff) return "sequence must be an unsigned 32-bit integer";
  if (!nullableNumber(voltage)) return "voltage must be a finite number or nil";
  if (!nullableNumber(current)) return "current must be a finite number or nil";
  if (!nullableInteger(satellites, 0, 255)) return "satellites must be 0..255 or nil";
  if (!nullableNumber(hdop) || (hdop !== null && hdop < 0)) return "hdop must be non-negative or nil";
  if (!nullableNumber(heading) || (heading !== null && (heading < 0 || heading >= 360))) return "heading must be in the range 0..<360 or nil";
  if (!nullableNumber(altitude)) return "altitude must be a finite number or nil";
  if (!nullableInteger(linkRssi, -127, 0)) return "link RSSI must be -127..0 dBm or nil";
  if (!Number.isInteger(flags) || flags < 0 || flags > 255) return "flags must be 0..255";
  return null;
}

export class TelemetryHub {
  private readonly states = new Map<string, TelemetryState>();

  connect(socket: WebSocket, crew: TelemetryCrew): void {
    const previous = this.states.get(crew.id);
    previous?.socket?.terminate();
    const state: TelemetryState = { socket, connected: true, telemetry: null, receivedAt: null };
    this.states.set(crew.id, state);

    socket.on("message", (data, isBinary) => {
      if (!isBinary) return socket.close(1003, "Binary MessagePack telemetry frames are required");
      let packet: unknown;
      try { packet = decode(data as Buffer); }
      catch { socket.close(1007, "Invalid MessagePack payload"); return; }
      const error = validateTelemetry(packet);
      if (error) return socket.close(1007, error.slice(0, 123));
      state.telemetry = [...(packet as TelemetryPacket)];
      state.receivedAt = Date.now();
    });
    socket.on("close", () => {
      if (state.socket !== socket) return;
      state.socket = null;
      state.connected = false;
    });
  }

  snapshot(crews: TelemetryCrew[], now = Date.now(), staleAfterMs = 5_000, offlineAfterMs = 10_000) {
    return crews.map(crew => {
      const state = this.states.get(crew.id);
      let snapshot: [number, number, ...TelemetryPacket] | null = null;
      if (state?.telemetry && state.receivedAt !== null) {
        const ageMs = Math.max(0, now - state.receivedAt);
        const status = !state.connected || ageMs >= offlineAfterMs ? TELEMETRY_STATUS.OFFLINE : ageMs >= staleAfterMs ? TELEMETRY_STATUS.STALE : TELEMETRY_STATUS.ONLINE;
        snapshot = [status, ageMs, ...state.telemetry];
      }
      return { id: crew.id, number: crew.number, name: crew.name, color: crew.color, snapshot };
    });
  }

  close(): void {
    for (const state of this.states.values()) state.socket?.terminate();
    this.states.clear();
  }
}
