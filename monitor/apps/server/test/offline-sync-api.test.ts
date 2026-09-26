import { readFileSync } from "node:fs";
import { randomUUID } from "node:crypto";
import { afterAll, beforeAll, describe, expect, it, vi } from "vitest";
import type { PGlite } from "@electric-sql/pglite";
import { eq } from "drizzle-orm";

const identity = vi.hoisted(() => ({ userId: "", crewId: "", groupId: "" }));
vi.mock("../src/db/index.js", async () => {
  const { PGlite } = await import("@electric-sql/pglite");
  const { drizzle } = await import("drizzle-orm/pglite");
  const schema = await import("../src/db/schema.js");
  const connection = new PGlite();
  return { connection, db: drizzle(connection, { schema }) };
});
vi.mock("../src/auth.js", async importOriginal => {
  const auth = await importOriginal<typeof import("../src/auth.js")>();
  return { ...auth, loadActor: async () => ({ ...identity, username: "offline", role: "CREW",
    groupName: "Group", crewNumber: 1, crewName: "Crew", crewColor: "green",
    userEnabled: true, groupEnabled: true, crewEnabled: true }) };
});
import { buildApp } from "../src/app.js";
import { db, connection } from "../src/db/index.js";
import { batteries, batteryTypes, crews, groups, measurements, syncOperations, users } from "../src/db/schema.js";

const pg = connection as unknown as PGlite;
let app: Awaited<ReturnType<typeof buildApp>>;
let batteryId: string;
let secondBatteryId: string;
const post = (payload: unknown) => app.inject({ method: "POST", url: "/api/crew/sync", payload: payload as object });
const base = () => ({ id: randomUUID(), batteryId, crewId: identity.crewId, occurredAt: "2026-09-20T12:00:00.000Z" });

beforeAll(async () => {
  const journal = JSON.parse(readFileSync(new URL("../drizzle/meta/_journal.json", import.meta.url), "utf8"));
  for (const entry of journal.entries) await pg.exec(readFileSync(new URL(`../drizzle/${entry.tag}.sql`, import.meta.url), "utf8"));
  const [group] = await db.insert(groups).values({ name: "Offline group" }).returning();
  const [crew] = await db.insert(crews).values({ groupId: group.id, number: 1, name: "Offline crew" }).returning();
  const [user] = await db.insert(users).values({ username: "offline", passwordHash: "test", role: "CREW", crewId: crew.id, groupId: group.id }).returning();
  Object.assign(identity, { userId: user.id, crewId: crew.id, groupId: group.id });
  const [type] = await db.insert(batteryTypes).values({ name: "12S", capacityAh: "20", minVoltage: "36", maxVoltage: "50.4", cellCount: 12, chemistry: "Li-ion" }).returning();
  const rows = await db.insert(batteries).values([1, 2].map(n => ({ crewId: crew.id, typeId: type.id, serialNumber: `offline-${n}`, label: `${n}` }))).returning();
  [batteryId, secondBatteryId] = rows.map(row => row.id);
  app = await buildApp();
}, 30000);
afterAll(async () => { await app?.close(); await pg.close(); });

describe.sequential("durable offline synchronization", () => {
  it("retries a committed measurement without duplicates and preserves its original time", async () => {
    const payload = { ...base(), kind: "measurement", cellVoltages: Array(12).fill(4.24), notes: "offline" };
    const first = await post(payload), retry = await post(payload);
    expect(first.statusCode).toBe(200); expect(retry.json()).toEqual(first.json());
    const rows = await db.select().from(measurements);
    expect(rows).toHaveLength(1);
    expect(rows[0].measuredAt.toISOString()).toBe(payload.occurredAt);
    expect(rows[0].cellVoltages).toEqual(Array(12).fill(4.24));
    expect((await post({ ...payload, notes: "different payload" })).statusCode).toBe(409);
    expect(await db.select().from(measurements)).toHaveLength(1);
  });
  it("deduplicates concurrent submissions", async () => {
    const payload = { ...base(), kind: "measurement", cellVoltages: Array(12).fill(4.1), notes: "concurrent" };
    const responses = await Promise.all([post(payload), post(payload)]);
    expect(responses.every(response => response.statusCode === 200)).toBe(true);
    expect(responses[0].json()).toEqual(responses[1].json());
    expect(await db.select().from(measurements)).toHaveLength(2);
  });
  it("does not store a receipt or measurement when a transaction fails", async () => {
    const failingApp = await buildApp({ rebuildCycleHistory: async () => { throw new Error("simulated failure"); } });
    const payload = { ...base(), kind: "measurement", cellVoltages: Array(12).fill(4.1) };
    const result = await failingApp.inject({ method: "POST", url: "/api/crew/sync", payload });
    expect(result.statusCode).toBe(500);
    expect(await db.select().from(syncOperations).where(eq(syncOperations.id, payload.id))).toHaveLength(0);
    expect(await db.select().from(measurements)).toHaveLength(2);
    await failingApp.close();
    expect((await post(payload)).statusCode).toBe(200);
  });
  it("rejects another crew and invalid or future measurements without writing", async () => {
    const payload = { ...base(), kind: "measurement", cellVoltages: Array(12).fill(4.25) };
    expect((await post(payload)).statusCode).toBe(400);
    expect((await post({ ...payload, crewId: randomUUID() })).statusCode).toBe(403);
    expect((await post({ ...payload, cellVoltages: Array(12).fill(4), occurredAt: "2099-01-01T00:00:00.000Z" })).statusCode).toBe(400);
    expect(await db.select().from(syncOperations).where(eq(syncOperations.id, payload.id))).toHaveLength(0);
  });
  it("applies explicit active state once and detects competing changes", async () => {
    const activate = { ...base(), kind: "active", active: true, expectedActiveId: null, expectedActiveSince: null };
    expect((await post(activate)).statusCode).toBe(200);
    expect((await post(activate)).statusCode).toBe(200);
    let [row] = await db.select().from(batteries).where(eq(batteries.id, batteryId));
    expect(row.activeSince?.toISOString()).toBe(activate.occurredAt);
    const stale = { ...base(), batteryId: secondBatteryId, kind: "active", active: true, expectedActiveId: null, expectedActiveSince: null };
    expect((await post(stale)).statusCode).toBe(409);
    expect(await db.select().from(syncOperations).where(eq(syncOperations.id, stale.id))).toHaveLength(0);
    const deactivate = { ...base(), kind: "active", active: false, expectedActiveId: batteryId, expectedActiveSince: activate.occurredAt };
    expect((await post(deactivate)).statusCode).toBe(200);
    expect((await post(activate)).statusCode).toBe(200); // Lost ACK must not reactivate it.
    [row] = await db.select().from(batteries).where(eq(batteries.id, batteryId));
    expect(row.activeSince).toBeNull();
  });
  it("rejects stale offline writes after a battery was archived", async () => {
    await db.update(batteries).set({ archivedAt: new Date() }).where(eq(batteries.id, secondBatteryId));
    const payload = { ...base(), batteryId: secondBatteryId, kind: "measurement", cellVoltages: Array(12).fill(4) };
    expect((await post(payload)).statusCode).toBe(409);
    expect(await db.select().from(syncOperations).where(eq(syncOperations.id, payload.id))).toHaveLength(0);
  });
});
