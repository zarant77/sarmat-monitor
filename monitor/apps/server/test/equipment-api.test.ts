import { readFileSync } from "node:fs";
import { PGlite } from "@electric-sql/pglite";
import { drizzle } from "drizzle-orm/pglite";
import { eq } from "drizzle-orm";
import { afterAll, beforeAll, describe, expect, it, vi } from "vitest";

vi.mock("../src/db/index.js", async () => {
  const schema = await import("../src/db/schema.js");
  const connection = new PGlite();
  return { connection, db: drizzle(connection, { schema }) };
});

vi.mock("../src/auth.js", async importOriginal => {
  const auth = await importOriginal<typeof import("../src/auth.js")>();
  return { ...auth, loadActor: async (request: { headers: Record<string, string | undefined> }) => {
    const role = (request.headers["x-test-role"] ?? "SUPER_ADMIN") as "SUPER_ADMIN" | "GROUP_ADMIN" | "CREW";
    return {
      userId: "00000000-0000-4000-8000-000000000001", username: "test", role,
      groupId: request.headers["x-test-group-id"] ?? null, groupName: null,
      crewId: request.headers["x-test-crew-id"] ?? null, crewNumber: null, crewName: null, crewColor: null,
      userEnabled: true, groupEnabled: true, crewEnabled: true
    };
  } };
});

import { buildApp } from "../src/app.js";
import { connection, db } from "../src/db/index.js";
import { crews, drones, groups, motors, users } from "../src/db/schema.js";

const pg = connection as unknown as PGlite;
const migration = (name: string) => readFileSync(new URL(`../drizzle/${name}.sql`, import.meta.url), "utf8");
let app: Awaited<ReturnType<typeof buildApp>>;
let groupA: string; let groupB: string; let crewA: string; let crewB: string;
const groupHeaders = (groupId: string) => ({ "x-test-role": "GROUP_ADMIN", "x-test-group-id": groupId });

beforeAll(async () => {
  for (const name of ["0000_initial", "0001_slippery_siren", "0002_milky_power_man", "0003_lethal_magneto", "0004_active_battery_and_event_deadband", "0005_battery_lifecycle", "0006_dynamic_charge_percent", "0007_offline_sync", "0008_battery_voltage_events", "0009_faithful_zaladane", "0010_flashy_sentry", "0011_bouncy_gorilla_man", "0012_bizarre_bastion", "0013_freezing_quasimodo", "0014_dizzy_selene"]) {
    await pg.exec(migration(name));
  }
  const [a, b] = await db.insert(groups).values([{ name: "Alpha" }, { name: "Bravo" }]).returning();
  groupA = a.id; groupB = b.id;
  const [first, second] = await db.insert(crews).values([
    { groupId: groupA, number: 1, name: "A1" },
    { groupId: groupB, number: 1, name: "B1" }
  ]).returning();
  crewA = first.id; crewB = second.id;
  await db.insert(users).values({ id: "00000000-0000-4000-8000-000000000001", username: "test", passwordHash: "test", role: "SUPER_ADMIN" });
  app = await buildApp();
}, 30000);

afterAll(async () => { await app?.close(); await pg.close(); });

describe.sequential("drone and motor registry API", () => {
  it("creates four- and six-motor drones and rejects every other count", async () => {
    for (const [name, motorCount] of [["Quad", 4], ["Hexa", 6]] as const) {
      const response = await app.inject({ method: "POST", url: "/api/drones", headers: groupHeaders(groupA), payload: { crewId: crewA, name, model: "Sarmat", motorCount, initialFlightSeconds: 3600 } });
      expect(response.statusCode).toBe(201);
      expect(response.json()).toMatchObject({ name, motorCount, groupId: groupA, totalFlightSeconds: 3600, status: "active" });
    }
    const invalid = await app.inject({ method: "POST", url: "/api/drones", headers: groupHeaders(groupA), payload: { crewId: crewA, name: "Octo", model: "Sarmat", motorCount: 8, initialFlightSeconds: 0 } });
    expect(invalid.statusCode).toBe(400);
  });

  it("rejects invalid flight time", async () => {
    for (const initialFlightSeconds of [-1, 1.5]) {
      const response = await app.inject({ method: "POST", url: "/api/drones", headers: groupHeaders(groupA), payload: { crewId: crewA, name: "Invalid", model: "Sarmat", motorCount: 4, initialFlightSeconds } });
      expect(response.statusCode).toBe(400);
    }
  });

  it("prevents cross-group access and assigning a foreign crew", async () => {
    const foreignAssignment = await app.inject({ method: "POST", url: "/api/drones", headers: groupHeaders(groupA), payload: { crewId: crewB, name: "Foreign", model: "Sarmat", motorCount: 4, initialFlightSeconds: 0 } });
    expect(foreignAssignment.statusCode).toBe(404);
    const list = await app.inject({ method: "GET", url: "/api/drones", headers: groupHeaders(groupB) });
    expect(list.statusCode).toBe(200);
    expect(list.json()).toHaveLength(0);
  });

  it("denies crew accounts administrative equipment access", async () => {
    const response = await app.inject({ method: "GET", url: "/api/drones", headers: { "x-test-role": "CREW", "x-test-group-id": groupA, "x-test-crew-id": crewA } });
    expect(response.statusCode).toBe(403);
  });

  it("enforces unique motor serials and scopes group administrators", async () => {
    const invalidType = await app.inject({ method: "POST", url: "/api/motors", headers: groupHeaders(groupA), payload: { serialNumber: "M-invalid", type: "CW", initialFlightSeconds: 0 } });
    expect(invalidType.statusCode).toBe(400);
    const created = await app.inject({ method: "POST", url: "/api/motors", headers: groupHeaders(groupA), payload: { serialNumber: "M-001", type: "CV", initialFlightSeconds: 7200 } });
    expect(created.statusCode).toBe(201);
    expect(created.json()).toMatchObject({ groupId: groupA, serialNumber: "M-001", type: "CV", status: "stock", totalFlightSeconds: 7200 });
    const duplicate = await app.inject({ method: "POST", url: "/api/motors", headers: groupHeaders(groupB), payload: { serialNumber: "M-001", type: "CCV", initialFlightSeconds: 0 } });
    expect(duplicate.statusCode).toBe(409);
    const foreignList = await app.inject({ method: "GET", url: "/api/motors", headers: groupHeaders(groupB) });
    expect(foreignList.json()).toHaveLength(0);
  });

  it("retires, hides, lists, and restores a motor", async () => {
    const [motor] = await db.select().from(motors).where(eq(motors.serialNumber, "M-001"));
    expect((await app.inject({ method: "POST", url: `/api/admin/motors/${motor.id}/retire`, headers: groupHeaders(groupA), payload: {} })).statusCode).toBe(200);
    expect((await app.inject({ method: "GET", url: "/api/motors?status=stock", headers: groupHeaders(groupA) })).json()).toHaveLength(0);
    const retired = await app.inject({ method: "GET", url: "/api/motors?status=retired", headers: groupHeaders(groupA) });
    expect(retired.json()).toHaveLength(1);
    expect(retired.json()[0].status).toBe("retired");
    expect((await app.inject({ method: "POST", url: `/api/admin/motors/${motor.id}/restore`, headers: groupHeaders(groupA), payload: {} })).statusCode).toBe(200);
  });

  it("installs, replaces, removes, and preserves motor installation history", async () => {
    const [quad] = await db.select().from(drones).where(eq(drones.name, "Quad"));
    const [first] = await db.select().from(motors).where(eq(motors.serialNumber, "M-001"));
    const created = await app.inject({ method: "POST", url: "/api/motors", headers: groupHeaders(groupA), payload: { serialNumber: "M-002", type: "CCV", initialFlightSeconds: 1800 } });
    const replacementId = created.json().id as string;

    const installed = await app.inject({ method: "POST", url: `/api/drones/${quad.id}/motor-positions/1`, headers: groupHeaders(groupA), payload: { motorId: first.id, notes: "Initial assembly" } });
    expect(installed.statusCode).toBe(200);
    expect(installed.json().installedMotorCount).toBe(1);
    expect(installed.json().slots[0]).toMatchObject({ positionNumber: 1, installation: { motorId: first.id, serialNumber: "M-001", type: "CV", active: true } });

    expect((await app.inject({ method: "POST", url: `/api/drones/${quad.id}/motor-positions/2`, headers: groupHeaders(groupA), payload: { motorId: first.id } })).statusCode).toBe(409);
    expect((await app.inject({ method: "POST", url: `/api/drones/${quad.id}/motor-positions/5`, headers: groupHeaders(groupA), payload: { motorId: replacementId } })).statusCode).toBe(400);
    expect((await app.inject({ method: "POST", url: `/api/admin/motors/${first.id}/retire`, headers: groupHeaders(groupA), payload: {} })).statusCode).toBe(409);
    expect((await app.inject({ method: "POST", url: `/api/admin/drones/${quad.id}/retire`, headers: groupHeaders(groupA), payload: {} })).statusCode).toBe(409);
    expect((await app.inject({ method: "PATCH", url: `/api/drones/${quad.id}`, headers: groupHeaders(groupA), payload: { motorCount: 6 } })).statusCode).toBe(409);

    const replaced = await app.inject({ method: "POST", url: `/api/drones/${quad.id}/motor-positions/1`, headers: groupHeaders(groupA), payload: { motorId: replacementId, notes: "Scheduled replacement" } });
    expect(replaced.statusCode).toBe(200);
    expect(replaced.json().installationHistory).toHaveLength(2);
    expect(replaced.json().installationHistory.filter((item: { active: boolean }) => item.active)).toHaveLength(1);
    expect((await app.inject({ method: "GET", url: `/api/motors/${first.id}`, headers: groupHeaders(groupA) })).json()).toMatchObject({ status: "stock", currentDroneId: null });
    expect((await app.inject({ method: "GET", url: `/api/motors/${replacementId}`, headers: groupHeaders(groupA) })).json()).toMatchObject({ type: "CCV", status: "installed", currentDroneId: quad.id, positionNumber: 1 });
    expect((await app.inject({ method: "GET", url: "/api/motors?status=installed", headers: groupHeaders(groupA) })).json()).toHaveLength(1);

    const removed = await app.inject({ method: "POST", url: `/api/drones/${quad.id}/motor-positions/1/remove`, headers: groupHeaders(groupA), payload: { notes: "Back to stock" } });
    expect(removed.statusCode).toBe(200);
    expect(removed.json().installedMotorCount).toBe(0);
    expect(removed.json().slots[0]).toMatchObject({ positionNumber: 1, installation: null });
    const motorHistory = (await app.inject({ method: "GET", url: `/api/motors/${replacementId}`, headers: groupHeaders(groupA) })).json();
    expect(motorHistory).toMatchObject({ status: "stock", installationHistory: [{ droneId: quad.id, removedByUsername: "test", removalNotes: "Back to stock" }] });
  });

  it("rejects installing a motor from another group", async () => {
    const foreign = await app.inject({ method: "POST", url: "/api/motors", headers: groupHeaders(groupB), payload: { serialNumber: "B-001", type: "CV", initialFlightSeconds: 0 } });
    const [quad] = await db.select().from(drones).where(eq(drones.name, "Quad"));
    const response = await app.inject({ method: "POST", url: `/api/drones/${quad.id}/motor-positions/1`, headers: groupHeaders(groupA), payload: { motorId: foreign.json().id } });
    expect(response.statusCode).toBe(404);
  });

  it("hides retired drones by default and includes them on request", async () => {
    const [drone] = await db.select().from(drones).where(eq(drones.name, "Quad"));
    await app.inject({ method: "POST", url: `/api/admin/drones/${drone.id}/retire`, headers: groupHeaders(groupA), payload: {} });
    expect((await app.inject({ method: "GET", url: "/api/drones", headers: groupHeaders(groupA) })).json()).toHaveLength(1);
    expect((await app.inject({ method: "GET", url: "/api/drones?includeRetired=true", headers: groupHeaders(groupA) })).json()).toHaveLength(2);
  });

  it("blocks deleting crews and groups that own equipment", async () => {
    expect((await app.inject({ method: "DELETE", url: `/api/crews/${crewA}`, headers: groupHeaders(groupA) })).statusCode).toBe(409);
    expect((await app.inject({ method: "DELETE", url: `/api/groups/${groupA}` })).statusCode).toBe(409);
  });
});
