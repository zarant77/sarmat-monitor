import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

const state = vi.hoisted(() => ({
  role: "CREW" as "SUPER_ADMIN" | "GROUP_ADMIN" | "CREW",
  actorGroupId: "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa" as string | null,
  batteryGroupId: "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",
  actorCrewId: "11111111-1111-4111-8111-111111111111" as string | null,
  batteryCrewId: "11111111-1111-4111-8111-111111111111",
  transactionCalls: 0,
  insertedMeasurement: null as Record<string, unknown> | null
}));

vi.mock("../src/db/index.js", () => {
  const chain = (result: (tableName?: string) => unknown[]) => {
    const value: Record<string, unknown> = {};
    let tableName: string | undefined;
    value.from = (table: Record<symbol, unknown>) => { tableName = String(table?.[Symbol.for("drizzle:Name")] ?? ""); return value; }; value.innerJoin = () => value; value.leftJoin = () => value;
    value.where = async () => result(tableName);
    return value;
  };
  const db = {
    select: (projection?: Record<string, unknown>) => projection && "session" in projection ? chain(() => [{
      session: { id: "session", userId: "user", tokenHash: "hash", expiresAt: new Date(Date.now() + 60_000), createdAt: new Date(), lastSeenAt: new Date() },
      user: { id: "user", username: "tester", passwordHash: "hash", role: state.role, groupId: state.actorGroupId, crewId: state.actorCrewId, enabled: true, createdAt: new Date(), updatedAt: new Date() },
      group: state.actorGroupId ? { id: state.actorGroupId, name: "Group", enabled: true } : null,
      crew: state.actorCrewId ? { id: state.actorCrewId, groupId: state.actorGroupId, number: 1, name: "Crew", color: "#B7EF55", notes: "", enabled: true, createdAt: new Date(), updatedAt: new Date() } : null
    }]) : projection && "battery" in projection ? chain(() => [{
      battery: { id: "battery", crewId: state.batteryCrewId, typeId: "type", serialNumber: "TEST", label: "Test", state: "ready", archivedAt: null, notes: "", createdAt: new Date(), updatedAt: new Date() },
      type: { id: "type", name: "Test type", capacityAh: "54", minVoltage: "36", maxVoltage: "50.4", cellCount: 12, chemistry: "LiPo", createdAt: new Date(), updatedAt: new Date() },
      crew: { id: state.batteryCrewId, groupId: state.batteryGroupId }, group: { id: state.batteryGroupId, name: "Group" }
    }]) : projection && "count" in projection ? chain(() => [{ count: 2 }]) : chain(tableName => tableName === "settings"
      ? [{ id: 1, warningCellDeltaV: "0.1", dangerCellDeltaV: "0.2", chargedThresholdPercent: 90, dischargedThresholdPercent: 20 }]
      : [{ id: photoSetId, batteryId: "battery", crewId: state.batteryCrewId, archivedAt: null }]),
    insert: () => ({ values: (values: Record<string, unknown>) => ({ returning: async () => {
      state.insertedMeasurement = values;
      return [{ id: "measurement", ...values, measuredAt: new Date("2026-01-01T00:00:00.000Z"), correctedAt: null, correctedByUserId: null }];
    } }) }),
    transaction: async (callback: (tx: unknown) => Promise<unknown>) => {
      state.transactionCalls += 1;
      const tx = {
        select: () => chain(tableName => tableName === "checker_photo_sets" ? [{ id: photoSetId, batteryId: "battery", createdByUserId: "user" }] : []),
        insert: () => ({ values: (values: Record<string, unknown>) => "mimeType" in values ? ({
          onConflictDoUpdate: () => ({ returning: async () => [{
            id: "image", batteryId: "battery", photoSetId: values.photoSetId, module: values.module,
            mimeType: values.mimeType, byteSize: values.byteSize, width: values.width, height: values.height,
            uploadedAt: new Date("2026-01-01T00:00:00.000Z")
          }] })
        }) : ({ onConflictDoNothing: async () => undefined }) })
      };
      return callback(tx);
    }
  };
  return { db, connection: { end: vi.fn() } };
});

import type { FastifyInstance } from "fastify";
import { buildApp } from "../src/app.js";

const ownCrew = "11111111-1111-4111-8111-111111111111";
const otherCrew = "22222222-2222-4222-8222-222222222222";
const photoSetId = "33333333-3333-4333-8333-333333333333";
const jpeg = Buffer.from([0xff, 0xd8, 0xff, 0xe0, 0x00, 0x10]);
let app: FastifyInstance;
const recognizedCells = Array.from({ length: 6 }, (_, index) => ({ index: index + 1, voltage: 4.2, confidence: "high" as const, score: 0.99 }));
const checkerRecognizer = { recognize: vi.fn(async (_image: Buffer, module: "A" | "B") => ({ module, cells: recognizedCells, confidence: 0.99, complete: true, issues: [] })) };
const rebuildCycleHistory = vi.fn(async () => undefined);

beforeEach(async () => {
  state.role = "CREW"; state.actorGroupId = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"; state.batteryGroupId = state.actorGroupId; state.actorCrewId = ownCrew; state.batteryCrewId = ownCrew; state.transactionCalls = 0; state.insertedMeasurement = null;
  checkerRecognizer.recognize.mockClear();
  rebuildCycleHistory.mockClear();
  app = await buildApp({ checkerRecognizer, rebuildCycleHistory });
});

afterEach(async () => { await app.close(); });

const upload = (module: "A" | "B" = "A") => app.inject({
  method: "POST",
  url: `/api/batteries/battery/checker-images/${module}?photoSetId=${photoSetId}`,
  headers: { cookie: "sbm_session=test", "content-type": "image/jpeg", "x-image-width": "1440", "x-image-height": "900" },
  payload: jpeg
});

describe("checker image upload endpoint", () => {
  it("allows a crew to upload a checker image for its own battery", async () => {
    const response = await upload();
    expect(response.statusCode).toBe(201);
    expect(response.json()).toMatchObject({ batteryId: "battery", photoSetId, module: "A", mimeType: "image/jpeg", width: 1440, height: 900, recognition: { cells: recognizedCells } });
    expect(response.json().recognition).not.toHaveProperty("totalVoltage");
    expect(state.transactionCalls).toBe(1);
  });

  it("does not allow crew A to upload an image for crew B battery", async () => {
    state.batteryCrewId = otherCrew;
    const response = await upload();
    expect(response.statusCode).toBe(404);
    expect(state.transactionCalls).toBe(0);
  });

  it("allows an administrator to upload for any crew", async () => {
    state.role = "SUPER_ADMIN"; state.actorGroupId = null; state.actorCrewId = null; state.batteryCrewId = otherCrew;
    const response = await upload();
    expect(response.statusCode).toBe(201);
  });

  it("accepts module A and B uploads concurrently in one photo set", async () => {
    const [moduleA, moduleB] = await Promise.all([upload("A"), upload("B")]);
    expect(moduleA.statusCode).toBe(201);
    expect(moduleB.statusCode).toBe(201);
    expect(moduleA.json().photoSetId).toBe(photoSetId);
    expect(moduleB.json().photoSetId).toBe(photoSetId);
    expect(state.transactionCalls).toBe(2);
  });

  it("rejects content that is not a valid image", async () => {
    const response = await app.inject({
      method: "POST",
      url: `/api/batteries/battery/checker-images/B?photoSetId=${photoSetId}`,
      headers: { cookie: "sbm_session=test", "content-type": "image/jpeg", "x-image-width": "1440", "x-image-height": "900" },
      payload: Buffer.from("not-an-image")
    });
    expect(response.statusCode).toBe(400);
    expect(state.transactionCalls).toBe(0);
  });

  it("authorizes and returns an A/B combined preview without saving a measurement", async () => {
    const response = await app.inject({
      method: "POST", url: "/api/batteries/battery/checker-preview", headers: { cookie: "sbm_session=test" },
      payload: { photoSetId, A: { cells: Array(6).fill(4.2) }, B: { cells: [4.19, 4.2, 4.2, 4.2, 4.2, 4.2] } }
    });
    expect(response.statusCode).toBe(200);
    expect(response.json()).toMatchObject({ cells: [...Array(6).fill(4.2), 4.19, 4.2, 4.2, 4.2, 4.2, 4.2], moduleATotalVoltage: 25.2, moduleBTotalVoltage: 25.19, combinedTotalVoltage: 50.39, health: "good" });
    expect(state.insertedMeasurement).toBeNull();

    state.batteryCrewId = otherCrew;
    const denied = await app.inject({ method: "POST", url: "/api/batteries/battery/checker-preview", headers: { cookie: "sbm_session=test" }, payload: { photoSetId, A: { cells: Array(6).fill(4.2) }, B: { cells: Array(6).fill(4.2) } } });
    expect(denied.statusCode).toBe(404);
  });

  it("saves the user-confirmed cells through the existing measurement model", async () => {
    const cells = [...Array(6).fill(4.2), 4.19, 4.2, 4.2, 4.2, 4.2, 4.2];
    const response = await app.inject({
      method: "POST", url: "/api/batteries/battery/measurements", headers: { cookie: "sbm_session=test" },
      payload: { photoSetId, cellVoltages: cells, notes: "confirmed" }
    });
    expect(response.statusCode).toBe(201);
    expect(response.json()).toMatchObject({ cellVoltages: cells, totalVoltage: 50.39, notes: "confirmed", health: "good" });
    expect(state.insertedMeasurement).toMatchObject({ photoSetId, cellVoltages: cells, notes: "confirmed" });
    expect(rebuildCycleHistory).toHaveBeenCalledWith("battery", { chargedThresholdPercent: 90, dischargedThresholdPercent: 20 });
  });
});
