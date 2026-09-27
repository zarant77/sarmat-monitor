import { randomUUID } from "node:crypto";
import { readFileSync } from "node:fs";
import { afterAll, beforeAll, expect, it, vi } from "vitest";
import type { PGlite } from "@electric-sql/pglite";
import { eq } from "drizzle-orm";

vi.mock("../src/db/index.js", async () => {
  const { PGlite } = await import("@electric-sql/pglite");
  const { drizzle } = await import("drizzle-orm/pglite");
  const schema = await import("../src/db/schema.js");
  const connection = new PGlite();
  return { connection, db: drizzle(connection, { schema }) };
});
vi.mock("../src/auth.js", async importOriginal => {
  const auth = await importOriginal<typeof import("../src/auth.js")>();
  return { ...auth, loadActor: async () => ({ userId: "test", username: "test", role: "SUPER_ADMIN",
    groupId: null, crewId: null, userEnabled: true, groupEnabled: true, crewEnabled: true }) };
});
import { buildApp } from "../src/app.js";
import { db, connection } from "../src/db/index.js";
import { batteries, batteryTypes, batteryVoltageEvents, crews, groups, measurements, cycleEvents } from "../src/db/schema.js";
const pg = connection as unknown as PGlite;
let app: Awaited<ReturnType<typeof buildApp>>;
let first: string, second: string, foreign: string, archived: string, crewId: string, groupId: string;
const headers = { authorization: "Bearer station-test" };
const list = () => app.inject({ method: "GET", url: "/station/batteries", headers });
const select = (batteryId: string, expectedActiveId: string | null = null, expectedActiveSince: string | null = null) =>
  app.inject({ method: "PUT", url: "/station/batteries/active", headers, payload: { batteryId, expectedActiveId, expectedActiveSince } });

beforeAll(async () => {
  for (const name of ["0000_initial", "0001_slippery_siren", "0002_milky_power_man", "0003_lethal_magneto", "0004_active_battery_and_event_deadband", "0005_battery_lifecycle", "0006_dynamic_charge_percent", "0007_offline_sync", "0008_battery_voltage_events"])
    await pg.exec(readFileSync(new URL(`../drizzle/${name}.sql`, import.meta.url), "utf8"));
  const [group] = await db.insert(groups).values({ name: "Station test" }).returning(); groupId = group.id;
  const [crew, other] = await db.insert(crews).values([
    { groupId, number: 1, name: "Own", secret: "station-test" }, { groupId, number: 2, name: "Other" }
  ]).returning(); crewId = crew.id;
  const [type] = await db.insert(batteryTypes).values({ name: "12S", capacityAh: "20", minVoltage: "36", maxVoltage: "50.4", cellCount: 12, chemistry: "Li-ion" }).returning();
  const rows = await db.insert(batteries).values([
    { crewId, typeId: type.id, serialNumber: "one", label: "One" },
    { crewId, typeId: type.id, serialNumber: "two", label: "Two" },
    { crewId: other.id, typeId: type.id, serialNumber: "other", label: "Other" },
    { crewId, typeId: type.id, serialNumber: "archived", label: "Archived", archivedAt: new Date() },
    { crewId, typeId: type.id, serialNumber: "retired", label: "Retired", state: "retired" }
  ]).returning();
  [first, second, foreign, archived] = rows.map(row => row.id);
  app = await buildApp();
}, 30000);
afterAll(async () => { await app?.close(); await pg.close(); });

it("requires a station secret and returns only operational batteries in its crew", async () => {
  expect((await app.inject({ url: "/station/batteries" })).statusCode).toBe(401);
  expect((await app.inject({ url: "/station/batteries", headers: { authorization: "Bearer wrong" } })).statusCode).toBe(401);
  const response = await list();
  expect(response.statusCode).toBe(200);
  expect(response.json().batteries.map((b: { id: string }) => b.id).sort()).toEqual([first, second].sort());
  expect(response.json().activeBatteryId).toBeNull();
});
it("sets an active battery, preserves it on repeat confirmation and rejects stale selections", async () => {
  const chosen = await select(first); expect(chosen.statusCode).toBe(200);
  const since = chosen.json().activeSince;
  expect((await list()).json()).toMatchObject({ activeBatteryId: first, activeSince: since });
  expect((await select(first, first, since)).json()).toEqual(chosen.json());
  expect((await select(second)).statusCode).toBe(409);
  expect((await select(second, first, "2020-01-01T00:00:00.000Z")).statusCode).toBe(409);
  expect((await select(second, first, since)).statusCode).toBe(200);
  expect((await list()).json().activeBatteryId).toBe(second);
});
it("rejects cross-crew, archived and malformed selections", async () => {
  expect((await select(foreign)).statusCode).toBe(404);
  expect((await select(archived)).statusCode).toBe(409);
  expect((await select("invalid")).statusCode).toBe(400);
});
it("stores voltage separately, deduplicates retries and binds delayed events to the selected battery", async () => {
  const current = (await list()).json();
  const sessionId = randomUUID();
  const bind = await app.inject({ method: "PUT", url: "/station/batteries/active", headers,
    payload: { batteryId: first, sessionId, expectedActiveId: current.activeBatteryId, expectedActiveSince: current.activeSince } });
  expect(bind.statusCode).toBe(200);
  expect(bind.json().sessionId).toBe(sessionId);
  expect((await select(second, first, bind.json().activeSince)).statusCode).toBe(200);
  const timestamp = new Date(Date.now() - 10_000).toISOString();
  const event = { id: randomUUID(), sessionId, type: "vehicle_disarmed", totalVoltage: 43.217,
    occurredAt: timestamp, measuredAt: timestamp };
  const post = (payload: unknown) => app.inject({ method: "POST", url: "/station/batteries/voltage-events", headers, payload });
  expect((await post(event)).statusCode).toBe(201);
  expect((await post(event)).json()).toEqual({ id: event.id, duplicate: true });
  expect((await post({ ...event, totalVoltage: 44 })).statusCode).toBe(409);
  const older = new Date(Date.now() - 60_000).toISOString();
  expect((await post({ ...event, id: randomUUID(), type: "vehicle_connected", totalVoltage: 50.1, occurredAt: older, measuredAt: older })).statusCode).toBe(201);
  const rows = await db.select().from(batteryVoltageEvents);
  expect(rows).toHaveLength(2);
  expect(rows.every(row => row.batteryId === first)).toBe(true);
  expect(await db.select().from(measurements)).toHaveLength(0);
  expect(await db.select().from(cycleEvents)).toHaveLength(0);
  const detail = await app.inject({ url: `/api/batteries/${first}` });
  expect(detail.statusCode).toBe(200);
  expect(detail.json().latestVoltageEvent).toMatchObject({ totalVoltage: 43.217, type: "vehicle_disarmed", source: "mission_planner" });
  expect(detail.json().latestMeasurement).toBeNull();
  const history = await app.inject({ url: `/api/batteries/${first}/history` });
  expect(history.statusCode).toBe(200);
  expect(history.json().items.map((row: { kind: string }) => row.kind)).toEqual(["vehicle_disarmed", "vehicle_connected"]);
  expect((await post({ ...event, id: randomUUID(), sessionId: randomUUID() })).statusCode).toBe(404);
  for (const totalVoltage of [0, -1, 1001]) expect((await post({ ...event, id: randomUUID(), totalVoltage })).statusCode).toBe(400);
  expect((await post({ ...event, id: randomUUID(), measuredAt: new Date(Date.now() + 600_000).toISOString() })).statusCode).toBe(400);
  expect((await post({ ...event, id: randomUUID(), type: "charge" })).statusCode).toBe(400);
  const nowActive = (await list()).json();
  expect((await app.inject({ method: "PUT", url: "/station/batteries/active", headers,
    payload: { batteryId: second, sessionId, expectedActiveId: second, expectedActiveSince: nowActive.activeSince } })).statusCode).toBe(409);
});

it("rejects disabled crews and groups", async () => {
  await db.update(crews).set({ enabled: false }).where(eq(crews.id, crewId));
  expect((await list()).statusCode).toBe(401);
  expect((await select(first)).statusCode).toBe(401);
  await db.update(crews).set({ enabled: true }).where(eq(crews.id, crewId));
  await db.update(groups).set({ enabled: false }).where(eq(groups.id, groupId));
  expect((await list()).statusCode).toBe(401);
});
