import { readFileSync } from "node:fs";
import { afterAll, beforeAll, describe, expect, it, vi } from "vitest";
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
  return { ...auth, loadActor: async (request: { headers: Record<string, string> }) => ({
    userId: "test-admin", username: "test", role: request.headers["x-test-role"] ?? "SUPER_ADMIN",
    groupId: null, groupName: null, crewId: null, crewNumber: null, crewName: null, crewColor: null,
    userEnabled: true, groupEnabled: true, crewEnabled: true
  }) };
});

import { buildApp } from "../src/app.js";
import { db, connection } from "../src/db/index.js";
import { batteries, batteryTypes, crews, cycleEvents, groups, transfers } from "../src/db/schema.js";

const pg = connection as unknown as PGlite;
const migration = (name: string) => readFileSync(new URL(`../drizzle/${name}.sql`, import.meta.url), "utf8");
let app: Awaited<ReturnType<typeof buildApp>>;
let batteryId: string;
let secondCrewId: string;
let legacyArchiveId: string;
let initialTransferId: string;
const lifecycle = (action: string) => app.inject({ method: "POST", url: `/api/admin/batteries/${batteryId}/${action}`, payload: { notes: `test ${action}` } });

beforeAll(async () => {
  for (const name of ["0000_initial", "0001_slippery_siren", "0002_milky_power_man", "0003_lethal_magneto", "0004_active_battery_and_event_deadband"]) await pg.exec(migration(name));
  const [group] = await db.insert(groups).values({ name: "Test group" }).returning();
  const [crew, other] = await db.insert(crews).values([{ groupId: group.id, number: 1, name: "First" }, { groupId: group.id, number: 2, name: "Second" }]).returning();
  secondCrewId = other.id;
  const [type] = await db.insert(batteryTypes).values({ name: "12S", capacityAh: "20", minVoltage: "36", maxVoltage: "50.4", cellCount: 12, chemistry: "Li-ion" }).returning();
  const [battery, archived] = await db.insert(batteries).values([
    { crewId: crew.id, typeId: type.id, serialNumber: "live", label: "Live" },
    { crewId: crew.id, typeId: type.id, serialNumber: "archived", label: "Archived", state: "retired", archivedAt: new Date() }
  ]).returning();
  batteryId = battery.id; legacyArchiveId = archived.id;
  const [transfer] = await db.insert(transfers).values({ batteryId, toCrewId: crew.id }).returning();
  initialTransferId = transfer.id;
  // Check that migration refusal is atomic and does not destroy unknown history.
  await pg.query("INSERT INTO cycle_events (battery_id, type) VALUES ($1, 'maintenance')", [batteryId]);
  await expect(pg.transaction(async tx => { await tx.exec(migration("0005_battery_lifecycle")); })).rejects.toThrow("Legacy battery events exist");
  await pg.exec("DELETE FROM cycle_events WHERE type = 'maintenance'");
  await pg.transaction(async tx => { await tx.exec(migration("0005_battery_lifecycle")); });
  app = await buildApp();
}, 30000);

afterAll(async () => { await app?.close(); await pg.close(); });

describe.sequential("battery lifecycle API and database migration", () => {
  it("preserves initial transfers and converts the old temporary archive", async () => {
    expect((await db.select().from(transfers))[0].id).toBe(initialTransferId);
    const [archived] = await db.select().from(batteries).where(eq(batteries.id, legacyArchiveId));
    expect(archived.state).toBe("storage");
    expect((await db.select().from(cycleEvents).where(eq(cycleEvents.batteryId, legacyArchiveId)))[0].type).toBe("archive");
  });

  it("saves all cells and transfers ownership with history", async () => {
    const measurement = await app.inject({ method: "POST", url: `/api/batteries/${batteryId}/measurements`, payload: { cellVoltages: Array(12).fill(4), notes: "initial" } });
    expect(measurement.statusCode).toBe(201);
    expect(measurement.json()).toMatchObject({ cellVoltages: Array(12).fill(4), totalVoltage: 48 });
    const transfer = await app.inject({ method: "POST", url: `/api/batteries/${batteryId}/transfer`, payload: { crewId: secondCrewId } });
    expect(transfer.statusCode).toBe(200);
    expect(transfer.json().toCrewId).toBe(secondCrewId);
  });

  it("requires admin access and rejects old event endpoints", async () => {
    const denied = await app.inject({ method: "POST", url: `/api/admin/batteries/${batteryId}/archive`, headers: { "x-test-role": "CREW" }, payload: {} });
    expect(denied.statusCode).toBe(403);
    expect((await app.inject({ method: "POST", url: `/api/batteries/${batteryId}/cycles`, payload: { type: "maintenance" } })).statusCode).toBe(404);
  });

  it("archives atomically, blocks operations and restores with a history record", async () => {
    await db.update(batteries).set({ activeSince: new Date() }).where(eq(batteries.id, batteryId));
    expect((await lifecycle("archive")).statusCode).toBe(200);
    const [archived] = await db.select().from(batteries).where(eq(batteries.id, batteryId));
    expect(archived).toMatchObject({ state: "storage", activeSince: null });
    for (const [endpoint, payload] of [["measurements", { cellVoltages: Array(12).fill(4) }], ["transfer", { crewId: secondCrewId }], ["toggle-active", {}]] as const) {
      expect((await app.inject({ method: "POST", url: `/api/batteries/${batteryId}/${endpoint}`, payload })).statusCode).toBe(409);
    }
    expect((await app.inject({ method: "PATCH", url: `/api/batteries/${batteryId}`, payload: { state: "ready" } })).statusCode).toBe(409);
    expect((await lifecycle("archive")).statusCode).toBe(409);
    expect((await lifecycle("restore")).statusCode).toBe(200);
    const events = await db.select().from(cycleEvents).where(eq(cycleEvents.batteryId, batteryId));
    expect(events.map(event => event.type).sort()).toEqual(["archive", "restore"]);
    expect(events.every(event => event.cycleDelta === 0 && !event.inferred)).toBe(true);
  });

  it("retires permanently and retains measurements and transfer history", async () => {
    expect((await lifecycle("retirement")).statusCode).toBe(200);
    expect((await lifecycle("restore")).statusCode).toBe(409);
    const detail = await app.inject({ method: "GET", url: `/api/batteries/${batteryId}` });
    expect(detail.statusCode).toBe(200);
    expect(detail.json()).toMatchObject({ state: "retired", cycleCount: 0 });
    expect(detail.json().measurements).toHaveLength(1);
    expect(detail.json().transfers).toHaveLength(2);
    expect(detail.json().cycleEvents.map((event: { type: string }) => event.type)).toContain("retirement");
  });
});
