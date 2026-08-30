import Fastify, { type FastifyInstance } from "fastify";
import cors from "@fastify/cors";
import cookie from "@fastify/cookie";
import websocket from "@fastify/websocket";
import { compare, hash } from "bcryptjs";
import { and, asc, desc, eq, isNull, sql } from "drizzle-orm";
import {
  batteryInputSchema, batteryTypeInputSchema, batteryTypeUpdateSchema, batteryUpdateSchema, checkerImageUploadSchema, checkerPreviewInputSchema, crewInputSchema, crewUpdateSchema, cycleEventInputSchema,
  credentialInputSchema, credentialUpdateSchema, groupAdminCredentialInputSchema, groupInputSchema, groupUpdateSchema, loginInputSchema, measurementCorrectionSchema,
  measurementInputSchema, thresholdInputSchema, transferInputSchema
} from "@sbm/shared";
import { ZodError } from "zod";
import { db } from "./db/index.js";
import { batteries, batteryTypes, checkerImages, checkerPhotoSets, crews, cycleEvents, groups, measurements, sessions, settings, transfers, users } from "./db/schema.js";
import { calculateCellHealth } from "./health.js";
import { MAX_CHECKER_IMAGE_BYTES, validateCheckerImage } from "./checker-images.js";
import { CheckerRecognitionError, combineCheckerReadings, type CheckerRecognizer } from "./checker-recognition.js";
import { createCheckerRecognizer } from "./ollama-checker-recognition.js";
import { assertActiveActor, assertCrewAccess, assertGroupAccess, assertGroupAdministrator, assertSuperAdmin, assertTransferAccess, createSession, effectiveCrewId, isAccountEnabled, loadActor, publicActor, revokeSession, type Actor } from "./auth.js";
import { rebuildInferredCycleEvents, type ChargeStateThresholds } from "./cycle-inference.js";
import { TELEMETRY_THRESHOLDS, TelemetryHub, type TelemetryCrew } from "./telemetry.js";

declare module "fastify" { interface FastifyRequest { telemetryCrew: TelemetryCrew | null } }

const number = (value: string | number | null) => value === null ? null : Number(value);
const iso = (value: Date) => value.toISOString();
const sumCellVoltages = (cells: number[]) => Math.round(cells.reduce((sum, voltage) => sum + voltage, 0) * 1000) / 1000;
const calculateChargePercent = (totalVoltage: number, minVoltage: number, maxVoltage: number) =>
  Math.round(Math.max(0, Math.min(100, (totalVoltage - minVoltage) / (maxVoltage - minVoltage) * 100)));

function mapMeasurement(row: typeof measurements.$inferSelect) {
  return {
    id: row.id, batteryId: row.batteryId, photoSetId: row.photoSetId, totalVoltage: Number(row.totalVoltage),
    cellVoltages: row.cellVoltages, minCellVoltage: Number(row.minCellVoltage),
    maxCellVoltage: Number(row.maxCellVoltage), cellDelta: Number(row.cellDelta),
    chargePercent: row.chargePercent, temperatureC: number(row.temperatureC), health: row.health,
    warningThresholdV: Number(row.warningThresholdV), dangerThresholdV: Number(row.dangerThresholdV),
    notes: row.notes, correctedAt: row.correctedAt ? iso(row.correctedAt) : null,
    correctedByUserId: row.correctedByUserId, measuredAt: iso(row.measuredAt)
  };
}

async function requireBattery(id: string, actor: Actor) {
  const [row] = await db.select({ battery: batteries, type: batteryTypes, crew: crews, group: groups }).from(batteries)
    .innerJoin(batteryTypes, eq(batteries.typeId, batteryTypes.id)).innerJoin(crews, eq(batteries.crewId, crews.id)).innerJoin(groups, eq(crews.groupId, groups.id))
    .where(eq(batteries.id, id));
  if (!row) throw Object.assign(new Error("Battery not found"), { statusCode: 404 });
  assertCrewAccess(actor, row.battery.crewId, row.crew.groupId);
  return { ...row.battery, groupId: row.crew.groupId, groupName: row.group.name, typeName: row.type.name, capacityAh: Number(row.type.capacityAh), minVoltage: Number(row.type.minVoltage), maxVoltage: Number(row.type.maxVoltage), cellCount: row.type.cellCount, chemistry: row.type.chemistry };
}

export async function buildApp(options: { checkerRecognizer?: CheckerRecognizer; rebuildCycleHistory?: (batteryId: string, thresholds: ChargeStateThresholds) => Promise<void> } = {}): Promise<FastifyInstance> {
  const app = Fastify({ logger: true });
  const checkerRecognizer = options.checkerRecognizer ?? createCheckerRecognizer();
  const rebuildCycleHistory = options.rebuildCycleHistory ?? rebuildInferredCycleEvents;
  await app.register(cors, { origin: process.env.CLIENT_ORIGIN?.split(",") ?? ["http://localhost:5173"], credentials: true });
  await app.register(cookie);
  await app.register(websocket, { options: { maxPayload: 4096 } });
  app.addContentTypeParser(["image/jpeg", "image/png", "image/webp"], { parseAs: "buffer", bodyLimit: MAX_CHECKER_IMAGE_BYTES }, (_request, body, done) => done(null, body));
  app.decorateRequest("actor", null);
  app.decorateRequest("telemetryCrew", null);
  const telemetry = new TelemetryHub();
  app.addHook("onClose", () => telemetry.close());

  app.setErrorHandler((error, _request, reply) => {
    if (error instanceof ZodError) return reply.status(400).send({ error: "Validation failed", issues: error.issues });
    if (error instanceof CheckerRecognitionError) return reply.status(error.statusCode).send({ error: error.message, code: error.code, issues: error.issues, partial: error.partial });
    const code = (error as { code?: string }).code;
    if (code === "23505") return reply.status(409).send({ error: "A record with that unique value already exists" });
    const status = (error as { statusCode?: number }).statusCode ?? 500;
    const message = error instanceof Error ? error.message : "Request failed";
    return reply.status(status).send({ error: status === 500 ? "Internal server error" : message });
  });

  app.get("/health", async () => ({ status: "ok" }));

  app.get("/ws/station", {
    websocket: true,
    preValidation: async request => {
      const match = /^Bearer\s+(.+)$/i.exec(request.headers.authorization ?? "");
      const secret = match?.[1]?.trim();
      if (!secret) throw Object.assign(new Error("Unauthorized"), { statusCode: 401 });
      const [row] = await db.select({ crew: crews, group: groups }).from(crews).innerJoin(groups, eq(crews.groupId, groups.id)).where(eq(crews.secret, secret));
      if (!row || !row.crew.enabled || !row.group.enabled || !row.crew.secret) throw Object.assign(new Error("Unauthorized"), { statusCode: 401 });
      request.telemetryCrew = { id: row.crew.id, groupId: row.crew.groupId, number: row.crew.number, name: row.crew.name, color: row.crew.color };
    }
  }, (socket, request) => telemetry.connect(socket, request.telemetryCrew!));

  app.post("/api/auth/login", async (request, reply) => {
    const data = loginInputSchema.parse(request.body);
    const [row] = await db.select({ user: users, crew: crews, group: groups }).from(users).leftJoin(crews, eq(users.crewId, crews.id)).leftJoin(groups, eq(users.groupId, groups.id)).where(eq(users.username, data.username));
    if (!row || !isAccountEnabled(row.user.role, row.user.enabled, row.group?.enabled ?? true, row.crew?.enabled ?? true) || !(await compare(data.password, row.user.passwordHash))) {
      throw Object.assign(new Error("Invalid username or password"), { statusCode: 401 });
    }
    await db.delete(sessions).where(eq(sessions.userId, row.user.id));
    await createSession(row.user.id, reply);
    return { id: row.user.id, username: row.user.username, role: row.user.role, groupId: row.user.groupId, groupName: row.group?.name ?? null, crewId: row.user.crewId, crewNumber: row.crew?.number ?? null, crewName: row.crew?.name ?? null, crewColor: row.crew?.color ?? null };
  });

  app.addHook("preHandler", async request => {
    if (!request.url.startsWith("/api/") || request.url.startsWith("/api/auth/login")) return;
    request.actor = await loadActor(request);
    assertActiveActor(request.actor);
  });

  app.get("/api/auth/me", async request => publicActor(request.actor!));
  app.post("/api/auth/logout", async (request, reply) => { await revokeSession(request, reply); return { ok: true }; });

  app.get<{ Querystring: { groupId?: string } }>("/api/telemetry", async request => {
    assertGroupAdministrator(request.actor);
    const actor = request.actor!;
    const groupId = actor.role === "SUPER_ADMIN" ? request.query.groupId : actor.groupId!;
    if (groupId) assertGroupAccess(actor, groupId);
    const rows = await db.select({ id: crews.id, groupId: crews.groupId, number: crews.number, name: crews.name, color: crews.color })
      .from(crews).where(groupId ? eq(crews.groupId, groupId) : undefined).orderBy(asc(crews.number));
    return { thresholds: TELEMETRY_THRESHOLDS, crews: telemetry.snapshot(rows) };
  });

  app.get("/api/groups", async request => {
    assertGroupAdministrator(request.actor);
    const actor = request.actor!;
    const groupFilter = actor.role === "SUPER_ADMIN" ? undefined : eq(groups.id, actor.groupId!);
    const rows = await db.select({
      group: groups,
      crewCount: sql<number>`count(distinct ${crews.id})::int`,
      batteryCount: sql<number>`count(distinct ${batteries.id})::int`,
      adminCount: sql<number>`count(distinct case when ${users.role} = 'GROUP_ADMIN' then ${users.id} end)::int`
    }).from(groups).leftJoin(crews, eq(crews.groupId, groups.id)).leftJoin(batteries, eq(batteries.crewId, crews.id)).leftJoin(users, eq(users.groupId, groups.id))
      .where(groupFilter).groupBy(groups.id).orderBy(asc(groups.name));
    return Promise.all(rows.map(async ({ group, crewCount, batteryCount, adminCount }) => {
      const [warning] = await db.select({ count: sql<number>`count(distinct ${batteries.id})::int` }).from(batteries).innerJoin(crews, eq(batteries.crewId, crews.id))
        .where(and(eq(crews.groupId, group.id), sql`(select ${measurements.health} from ${measurements} where ${measurements.batteryId} = ${batteries.id} order by ${measurements.measuredAt} desc limit 1) in ('warning', 'danger')`));
      return { ...group, crewCount, batteryCount, warningCount: warning.count, adminCount, createdAt: iso(group.createdAt), updatedAt: iso(group.updatedAt) };
    }));
  });

  app.get<{ Params: { id: string } }>("/api/groups/:id", async request => {
    assertGroupAdministrator(request.actor); assertGroupAccess(request.actor!, request.params.id);
    const [group] = await db.select().from(groups).where(eq(groups.id, request.params.id));
    if (!group) throw Object.assign(new Error("Group not found"), { statusCode: 404 });
    return { ...group, createdAt: iso(group.createdAt), updatedAt: iso(group.updatedAt) };
  });

  app.post("/api/groups", async (request, reply) => {
    assertSuperAdmin(request.actor);
    const [group] = await db.insert(groups).values(groupInputSchema.parse(request.body)).returning();
    return reply.status(201).send({ ...group, crewCount: 0, batteryCount: 0, warningCount: 0, adminCount: 0, createdAt: iso(group.createdAt), updatedAt: iso(group.updatedAt) });
  });

  app.patch<{ Params: { id: string } }>("/api/groups/:id", async request => {
    assertSuperAdmin(request.actor);
    const [group] = await db.update(groups).set({ ...groupUpdateSchema.parse(request.body), updatedAt: new Date() }).where(eq(groups.id, request.params.id)).returning();
    if (!group) throw Object.assign(new Error("Group not found"), { statusCode: 404 });
    if (!group.enabled) await db.delete(sessions).where(sql`${sessions.userId} in (select ${users.id} from ${users} where ${users.groupId} = ${group.id})`);
    return { ...group, createdAt: iso(group.createdAt), updatedAt: iso(group.updatedAt) };
  });

  app.delete<{ Params: { id: string } }>("/api/groups/:id", async (request, reply) => {
    assertSuperAdmin(request.actor);
    const [counts] = await db.select({ crews: sql<number>`count(distinct ${crews.id})::int`, users: sql<number>`count(distinct ${users.id})::int` }).from(groups)
      .leftJoin(crews, eq(crews.groupId, groups.id)).leftJoin(users, eq(users.groupId, groups.id)).where(eq(groups.id, request.params.id)).groupBy(groups.id);
    if (!counts) throw Object.assign(new Error("Group not found"), { statusCode: 404 });
    if (counts.crews || counts.users) throw Object.assign(new Error("Group can only be deleted when it has no crews or accounts"), { statusCode: 409 });
    await db.delete(groups).where(eq(groups.id, request.params.id)); return reply.status(204).send();
  });

  app.get<{ Querystring: { groupId?: string } }>("/api/crews", async request => {
    assertGroupAdministrator(request.actor);
    const groupId = request.actor!.role === "SUPER_ADMIN" ? request.query.groupId : request.actor!.groupId!;
    const rows = await db.select({ crew: crews, batteryCount: sql<number>`count(distinct ${batteries.id})::int`, userCount: sql<number>`count(distinct ${users.id})::int` })
      .from(crews).leftJoin(batteries, eq(batteries.crewId, crews.id)).leftJoin(users, eq(users.crewId, crews.id)).where(groupId ? eq(crews.groupId, groupId) : undefined).groupBy(crews.id).orderBy(asc(crews.number));
    return rows.map(({ crew, batteryCount, userCount }) => ({ ...crew, batteryCount, userCount, createdAt: iso(crew.createdAt), updatedAt: iso(crew.updatedAt) }));
  });

  app.post("/api/crews", async (request, reply) => {
    assertGroupAdministrator(request.actor);
    const data = crewInputSchema.parse(request.body);
    const groupId = request.actor!.role === "SUPER_ADMIN" ? data.groupId : request.actor!.groupId;
    if (!groupId) throw Object.assign(new Error("A group assignment is required"), { statusCode: 400 });
    assertGroupAccess(request.actor!, groupId);
    const [group] = await db.select().from(groups).where(eq(groups.id, groupId));
    if (!group || !group.enabled) throw Object.assign(new Error("Assigned group is not available"), { statusCode: 400 });
    const { groupId: _groupId, ...crewData } = data;
    const [crew] = await db.insert(crews).values({ ...crewData, groupId }).returning();
    return reply.status(201).send({ ...crew, batteryCount: 0, createdAt: iso(crew.createdAt), updatedAt: iso(crew.updatedAt) });
  });

  app.patch<{ Params: { id: string } }>("/api/crews/:id", async (request) => {
    assertGroupAdministrator(request.actor);
    const [current] = await db.select().from(crews).where(eq(crews.id, request.params.id));
    if (!current) throw Object.assign(new Error("Crew not found"), { statusCode: 404 });
    assertGroupAccess(request.actor!, current.groupId);
    const data = crewUpdateSchema.parse(request.body);
    const [crew] = await db.update(crews).set({ ...data, updatedAt: new Date() }).where(eq(crews.id, request.params.id)).returning();
    if (!crew) throw Object.assign(new Error("Crew not found"), { statusCode: 404 });
    return { ...crew, createdAt: iso(crew.createdAt), updatedAt: iso(crew.updatedAt) };
  });

  app.delete<{ Params: { id: string } }>("/api/crews/:id", async (request, reply) => {
    assertGroupAdministrator(request.actor);
    const [target] = await db.select().from(crews).where(eq(crews.id, request.params.id));
    if (!target) throw Object.assign(new Error("Crew not found"), { statusCode: 404 });
    assertGroupAccess(request.actor!, target.groupId);
    const [counts] = await db.select({ batteries: sql<number>`count(distinct ${batteries.id})::int`, users: sql<number>`count(distinct ${users.id})::int` })
      .from(crews).leftJoin(batteries, eq(batteries.crewId, crews.id)).leftJoin(users, eq(users.crewId, crews.id)).where(eq(crews.id, request.params.id)).groupBy(crews.id);
    if (!counts) throw Object.assign(new Error("Crew not found"), { statusCode: 404 });
    if (counts.batteries || counts.users) throw Object.assign(new Error("Crew can only be deleted after its batteries and credentials are removed or reassigned"), { statusCode: 409 });
    await db.delete(crews).where(eq(crews.id, request.params.id));
    return reply.status(204).send();
  });

  app.get<{ Querystring: { crewId?: string; groupId?: string } }>("/api/admin/users", async request => {
    assertGroupAdministrator(request.actor);
    const actor = request.actor!;
    const groupId = actor.role === "SUPER_ADMIN" ? request.query.groupId : actor.groupId!;
    const filters = and(request.query.crewId ? eq(users.crewId, request.query.crewId) : undefined, groupId ? eq(users.groupId, groupId) : undefined);
    const rows = await db.select({ user: users, crew: crews, group: groups }).from(users).leftJoin(crews, eq(users.crewId, crews.id)).leftJoin(groups, eq(users.groupId, groups.id))
      .where(filters).orderBy(asc(users.username));
    return rows.map(({ user, crew, group }) => ({ id: user.id, username: user.username, role: user.role, groupId: user.groupId, groupName: group?.name ?? null, crewId: user.crewId, crewNumber: crew?.number ?? null, crewName: crew?.name ?? null, crewColor: crew?.color ?? null, enabled: user.enabled, createdAt: iso(user.createdAt), updatedAt: iso(user.updatedAt) }));
  });

  app.post("/api/admin/group-users", async (request, reply) => {
    assertSuperAdmin(request.actor);
    const data = groupAdminCredentialInputSchema.parse(request.body);
    const [group] = await db.select().from(groups).where(eq(groups.id, data.groupId));
    if (!group) throw Object.assign(new Error("Group not found"), { statusCode: 404 });
    const [user] = await db.insert(users).values({ username: data.username, passwordHash: await hash(data.password, 12), role: "GROUP_ADMIN", groupId: group.id, crewId: null, enabled: data.enabled }).returning();
    return reply.status(201).send({ id: user.id, username: user.username, role: user.role, groupId: group.id, groupName: group.name, crewId: null, crewNumber: null, crewName: null, crewColor: null, enabled: user.enabled, createdAt: iso(user.createdAt), updatedAt: iso(user.updatedAt) });
  });

  app.post<{ Params: { id: string } }>("/api/admin/crews/:id/users", async (request, reply) => {
    assertGroupAdministrator(request.actor);
    const data = credentialInputSchema.parse(request.body);
    const [crew] = await db.select().from(crews).where(eq(crews.id, request.params.id));
    if (!crew) throw Object.assign(new Error("Crew not found"), { statusCode: 404 });
    assertGroupAccess(request.actor!, crew.groupId);
    const [user] = await db.insert(users).values({ username: data.username, passwordHash: await hash(data.password, 12), role: "CREW", groupId: crew.groupId, crewId: crew.id, enabled: data.enabled }).returning();
    return reply.status(201).send({ id: user.id, username: user.username, role: user.role, groupId: crew.groupId, groupName: null, crewId: user.crewId, crewNumber: crew.number, crewName: crew.name, crewColor: crew.color, enabled: user.enabled, createdAt: iso(user.createdAt), updatedAt: iso(user.updatedAt) });
  });

  app.patch<{ Params: { id: string } }>("/api/admin/users/:id", async request => {
    assertGroupAdministrator(request.actor);
    const data = credentialUpdateSchema.parse(request.body);
    const [current] = await db.select().from(users).where(eq(users.id, request.params.id));
    if (!current) throw Object.assign(new Error("User not found"), { statusCode: 404 });
    if (current.role === "SUPER_ADMIN") assertSuperAdmin(request.actor);
    if (current.groupId) assertGroupAccess(request.actor!, current.groupId);
    if (request.actor!.role === "GROUP_ADMIN" && current.role !== "CREW") throw Object.assign(new Error("Resource not found"), { statusCode: 404 });
    const values = { username: data.username, enabled: data.enabled, passwordHash: data.password ? await hash(data.password, 12) : undefined, updatedAt: new Date() };
    const [user] = await db.update(users).set(values).where(eq(users.id, request.params.id)).returning();
    if (!user) throw Object.assign(new Error("User not found"), { statusCode: 404 });
    await db.delete(sessions).where(eq(sessions.userId, user.id));
    return { id: user.id, username: user.username, role: user.role, groupId: user.groupId, groupName: null, crewId: user.crewId, crewNumber: null, crewName: null, crewColor: null, enabled: user.enabled, createdAt: iso(user.createdAt), updatedAt: iso(user.updatedAt) };
  });

  app.delete<{ Params: { id: string } }>("/api/admin/users/:id", async (request, reply) => {
    assertGroupAdministrator(request.actor);
    const [user] = await db.select().from(users).where(eq(users.id, request.params.id));
    if (!user) throw Object.assign(new Error("User not found"), { statusCode: 404 });
    if (user.role === "SUPER_ADMIN") assertSuperAdmin(request.actor);
    if (user.groupId) assertGroupAccess(request.actor!, user.groupId);
    if (request.actor!.role === "GROUP_ADMIN" && user.role !== "CREW") throw Object.assign(new Error("Resource not found"), { statusCode: 404 });
    if (user.id === request.actor!.userId) throw Object.assign(new Error("You cannot delete your active administrator account"), { statusCode: 409 });
    await db.delete(users).where(eq(users.id, user.id));
    return reply.status(204).send();
  });

  app.get("/api/battery-types", async request => {
    assertGroupAdministrator(request.actor);
    const rows = await db.select({ type: batteryTypes, batteryCount: sql<number>`count(${batteries.id})::int` })
      .from(batteryTypes).leftJoin(batteries, eq(batteries.typeId, batteryTypes.id)).groupBy(batteryTypes.id).orderBy(asc(batteryTypes.name));
    return rows.map(({ type, batteryCount }) => ({ ...type, capacityAh: Number(type.capacityAh), minVoltage: Number(type.minVoltage), maxVoltage: Number(type.maxVoltage), batteryCount, createdAt: iso(type.createdAt), updatedAt: iso(type.updatedAt) }));
  });

  app.post("/api/battery-types", async (request, reply) => {
    assertSuperAdmin(request.actor);
    const data = batteryTypeInputSchema.parse(request.body);
    const [type] = await db.insert(batteryTypes).values({ ...data, capacityAh: data.capacityAh.toString(), minVoltage: data.minVoltage.toString(), maxVoltage: data.maxVoltage.toString() }).returning();
    return reply.status(201).send({ ...type, capacityAh: Number(type.capacityAh), minVoltage: Number(type.minVoltage), maxVoltage: Number(type.maxVoltage), batteryCount: 0, createdAt: iso(type.createdAt), updatedAt: iso(type.updatedAt) });
  });

  app.patch<{ Params: { id: string } }>("/api/battery-types/:id", async request => {
    assertSuperAdmin(request.actor);
    const data = batteryTypeUpdateSchema.parse(request.body);
    const [current] = await db.select().from(batteryTypes).where(eq(batteryTypes.id, request.params.id));
    if (!current) throw Object.assign(new Error("Battery type not found"), { statusCode: 404 });
    const minVoltage = data.minVoltage ?? Number(current.minVoltage), maxVoltage = data.maxVoltage ?? Number(current.maxVoltage);
    if (maxVoltage <= minVoltage) throw Object.assign(new Error("Maximum voltage must be greater than minimum voltage"), { statusCode: 400 });
    const [type] = await db.update(batteryTypes).set({ ...data, capacityAh: data.capacityAh?.toString(), minVoltage: data.minVoltage?.toString(), maxVoltage: data.maxVoltage?.toString(), updatedAt: new Date() }).where(eq(batteryTypes.id, request.params.id)).returning();
    return { ...type, capacityAh: Number(type.capacityAh), minVoltage: Number(type.minVoltage), maxVoltage: Number(type.maxVoltage), createdAt: iso(type.createdAt), updatedAt: iso(type.updatedAt) };
  });

  app.delete<{ Params: { id: string } }>("/api/battery-types/:id", async (request, reply) => {
    assertSuperAdmin(request.actor);
    const [usage] = await db.select({ count: sql<number>`count(*)::int` }).from(batteries).where(eq(batteries.typeId, request.params.id));
    if (usage.count) throw Object.assign(new Error("Battery type is in use and cannot be deleted"), { statusCode: 409 });
    const [deleted] = await db.delete(batteryTypes).where(eq(batteryTypes.id, request.params.id)).returning();
    if (!deleted) throw Object.assign(new Error("Battery type not found"), { statusCode: 404 });
    return reply.status(204).send();
  });

  app.get<{ Querystring: { crewId?: string; groupId?: string; includeArchived?: string } }>("/api/batteries", async (request) => {
    const actor = request.actor!;
    const crewId = effectiveCrewId(actor, request.query.crewId);
    const groupId = actor.role === "SUPER_ADMIN" ? request.query.groupId : actor.role === "GROUP_ADMIN" ? actor.groupId! : undefined;
    const filters = and(
      crewId ? eq(batteries.crewId, crewId) : undefined,
      groupId ? eq(crews.groupId, groupId) : undefined,
      actor.role !== "CREW" && request.query.includeArchived === "true" ? undefined : isNull(batteries.archivedAt)
    );
    const rows = await db.select({ battery: batteries, type: batteryTypes, groupId: groups.id, groupName: groups.name, crewNumber: crews.number, crewName: crews.name, crewColor: crews.color,
      cycleCount: sql<number>`coalesce((select sum(${cycleEvents.cycleDelta}) from ${cycleEvents} where ${cycleEvents.batteryId} = ${batteries.id}), 0)::int`
    }).from(batteries).innerJoin(crews, eq(batteries.crewId, crews.id)).innerJoin(groups, eq(crews.groupId, groups.id)).innerJoin(batteryTypes, eq(batteries.typeId, batteryTypes.id)).where(filters).orderBy(asc(batteries.label));
    return Promise.all(rows.map(async row => {
      const [latest] = await db.select().from(measurements).where(eq(measurements.batteryId, row.battery.id)).orderBy(desc(measurements.measuredAt)).limit(1);
      return { ...row.battery, groupId: row.groupId, groupName: row.groupName, typeName: row.type.name, capacityAh: Number(row.type.capacityAh), minVoltage: Number(row.type.minVoltage), maxVoltage: Number(row.type.maxVoltage), cellCount: row.type.cellCount, chemistry: row.type.chemistry, crewNumber: row.crewNumber, crewName: row.crewName, crewColor: row.crewColor,
        cycleCount: row.cycleCount, latestMeasurement: latest ? mapMeasurement(latest) : null,
        createdAt: iso(row.battery.createdAt), updatedAt: iso(row.battery.updatedAt) };
    }));
  });

  app.post("/api/batteries", async (request, reply) => {
    assertGroupAdministrator(request.actor);
    const data = batteryInputSchema.parse(request.body);
    const crewId = effectiveCrewId(request.actor!, data.crewId);
    if (!crewId) throw Object.assign(new Error("A crew assignment is required"), { statusCode: 400 });
    const [assignedCrew] = await db.select().from(crews).where(eq(crews.id, crewId));
    if (!assignedCrew || !assignedCrew.enabled) throw Object.assign(new Error("Assigned crew is not available"), { statusCode: 400 });
    assertGroupAccess(request.actor!, assignedCrew.groupId);
    const [batteryType] = await db.select().from(batteryTypes).where(eq(batteryTypes.id, data.typeId));
    if (!batteryType) throw Object.assign(new Error("Battery type not found"), { statusCode: 400 });
    const { crewId: _ignoredCrewId, ...rest } = data;
    const [battery] = await db.transaction(async tx => {
      const inserted = await tx.insert(batteries).values({ ...rest, crewId }).returning();
      await tx.insert(transfers).values({ batteryId: inserted[0].id, toCrewId: crewId, notes: "Battery registered" });
      return inserted;
    });
    return reply.status(201).send({ ...battery, typeName: batteryType.name, capacityAh: Number(batteryType.capacityAh), minVoltage: Number(batteryType.minVoltage), maxVoltage: Number(batteryType.maxVoltage), cellCount: batteryType.cellCount, chemistry: batteryType.chemistry, createdAt: iso(battery.createdAt), updatedAt: iso(battery.updatedAt) });
  });

  app.get<{ Params: { id: string } }>("/api/batteries/:id", async request => {
    const battery = await requireBattery(request.params.id, request.actor!);
    const [crew] = await db.select().from(crews).where(eq(crews.id, battery.crewId));
    const measurementRows = await db.select().from(measurements).where(eq(measurements.batteryId, battery.id)).orderBy(desc(measurements.measuredAt));
    const eventRows = await db.select().from(cycleEvents).where(eq(cycleEvents.batteryId, battery.id)).orderBy(desc(cycleEvents.occurredAt));
    const transferRows = await db.select().from(transfers).where(eq(transfers.batteryId, battery.id)).orderBy(desc(transfers.transferredAt));
    const crewRows = await db.select().from(crews);
    const crewNames = new Map(crewRows.map(item => [item.id, item.name]));
    const cycleCount = eventRows.reduce((sum, event) => sum + event.cycleDelta, 0);
    return {
      ...battery, crewNumber: crew.number, crewName: crew.name, crewColor: crew.color,
      cycleCount, latestMeasurement: measurementRows[0] ? mapMeasurement(measurementRows[0]) : null,
      measurements: measurementRows.map(mapMeasurement),
      cycleEvents: eventRows.map(e => ({ ...e, occurredAt: iso(e.occurredAt) })),
      transfers: transferRows.map(transfer => ({ ...transfer, fromCrewName: transfer.fromCrewId ? crewNames.get(transfer.fromCrewId) ?? null : null, toCrewName: crewNames.get(transfer.toCrewId) ?? "Unknown crew", transferredAt: iso(transfer.transferredAt) })),
      createdAt: iso(battery.createdAt), updatedAt: iso(battery.updatedAt)
    };
  });

  app.patch<{ Params: { id: string } }>("/api/batteries/:id", async request => {
    assertGroupAdministrator(request.actor);
    await requireBattery(request.params.id, request.actor!);
    const data = batteryUpdateSchema.parse(request.body);
    if (data.typeId) {
      const [type] = await db.select().from(batteryTypes).where(eq(batteryTypes.id, data.typeId));
      if (!type) throw Object.assign(new Error("Battery type not found"), { statusCode: 400 });
    }
    const [battery] = await db.update(batteries).set({ ...data, updatedAt: new Date() }).where(eq(batteries.id, request.params.id)).returning();
    if (!battery) throw Object.assign(new Error("Battery not found"), { statusCode: 404 });
    const enriched = await requireBattery(battery.id, request.actor!);
    return { ...enriched, createdAt: iso(enriched.createdAt), updatedAt: iso(enriched.updatedAt) };
  });

  app.post<{ Params: { id: string } }>("/api/batteries/:id/transfer", async (request) => {
    assertGroupAdministrator(request.actor);
    const data = transferInputSchema.parse(request.body);
    const current = await requireBattery(request.params.id, request.actor!);
    const [targetCrew] = await db.select().from(crews).where(eq(crews.id, data.crewId));
    if (!targetCrew || !targetCrew.enabled) throw Object.assign(new Error("Target crew is not available"), { statusCode: 400 });
    assertTransferAccess(request.actor!, current.groupId, targetCrew.groupId);
    if (current.crewId === data.crewId) throw Object.assign(new Error("Battery already belongs to this crew"), { statusCode: 400 });
    return db.transaction(async tx => {
      await tx.update(batteries).set({ crewId: data.crewId, updatedAt: new Date() }).where(eq(batteries.id, current.id));
      const [event] = await tx.insert(transfers).values({ batteryId: current.id, fromCrewId: current.crewId, toCrewId: data.crewId, notes: data.notes ?? "" }).returning();
      return { ...event, transferredAt: iso(event.transferredAt) };
    });
  });

  app.post<{ Params: { id: string } }>("/api/batteries/:id/measurements", async (request, reply) => {
    const data = measurementInputSchema.parse(request.body);
    const battery = await requireBattery(request.params.id, request.actor!);
    if (data.cellVoltages.length !== battery.cellCount) {
      throw Object.assign(new Error(`Expected ${battery.cellCount} cell voltages, received ${data.cellVoltages.length}`), { statusCode: 400 });
    }
    const [configuration] = await db.select().from(settings).where(eq(settings.id, 1));
    const warning = Number(configuration.warningCellDeltaV);
    const danger = Number(configuration.dangerCellDeltaV);
    const result = calculateCellHealth(data.cellVoltages, warning, danger);
    const totalVoltage = sumCellVoltages(data.cellVoltages);
    const chargePercent = calculateChargePercent(totalVoltage, battery.minVoltage, battery.maxVoltage);
    if (data.photoSetId) {
      const [photoSet] = await db.select().from(checkerPhotoSets).where(and(eq(checkerPhotoSets.id, data.photoSetId), eq(checkerPhotoSets.batteryId, battery.id)));
      if (!photoSet) throw Object.assign(new Error("Checker photo set does not belong to this battery"), { statusCode: 400 });
      const [photoCount] = await db.select({ count: sql<number>`count(*)::int` }).from(checkerImages).where(eq(checkerImages.photoSetId, data.photoSetId));
      if (photoCount.count !== 2) throw Object.assign(new Error("Both checker photos A and B are required"), { statusCode: 400 });
      if (battery.cellCount === 12) combineCheckerReadings(data.photoSetId, data.cellVoltages.slice(0, 6), data.cellVoltages.slice(6, 12), warning, danger, battery.minVoltage, battery.maxVoltage);
    }
    const [measurement] = await db.insert(measurements).values({
      batteryId: battery.id, photoSetId: data.photoSetId, totalVoltage: totalVoltage.toString(), cellVoltages: data.cellVoltages,
      minCellVoltage: result.minCellVoltage.toString(), maxCellVoltage: result.maxCellVoltage.toString(),
      cellDelta: result.cellDelta.toString(), chargePercent,
      temperatureC: null, health: result.health,
      warningThresholdV: warning.toString(), dangerThresholdV: danger.toString(), notes: data.notes
    }).returning();
    await rebuildCycleHistory(battery.id, { chargedThresholdPercent: configuration.chargedThresholdPercent, dischargedThresholdPercent: configuration.dischargedThresholdPercent });
    return reply.status(201).send(mapMeasurement(measurement));
  });

  app.post<{ Params: { id: string; module: string }; Querystring: { photoSetId?: string }; Headers: { "x-image-width"?: string; "x-image-height"?: string } }>("/api/batteries/:id/checker-images/:module", async (request, reply) => {
    const battery = await requireBattery(request.params.id, request.actor!);
    if (battery.archivedAt) throw Object.assign(new Error("Archived batteries cannot receive checker images"), { statusCode: 409 });
    const mimeType = String(request.headers["content-type"] ?? "").split(";")[0].toLowerCase();
    const metadata = checkerImageUploadSchema.parse({
      photoSetId: request.query.photoSetId,
      module: request.params.module,
      width: request.headers["x-image-width"],
      height: request.headers["x-image-height"]
    });
    const imageData = validateCheckerImage(request.body, mimeType);
    const recognition = await checkerRecognizer.recognize(imageData, metadata.module, {
      minCellVoltage: battery.minVoltage / battery.cellCount,
      maxCellVoltage: battery.maxVoltage / battery.cellCount
    });

    const saved = await db.transaction(async tx => {
      await tx.insert(checkerPhotoSets).values({ id: metadata.photoSetId, batteryId: battery.id, createdByUserId: request.actor!.userId })
        .onConflictDoNothing({ target: checkerPhotoSets.id });
      const [photoSet] = await tx.select().from(checkerPhotoSets).where(eq(checkerPhotoSets.id, metadata.photoSetId));
      if (!photoSet || photoSet.batteryId !== battery.id) {
        throw Object.assign(new Error("Checker photo set belongs to another battery"), { statusCode: 409 });
      }
      const [image] = await tx.insert(checkerImages).values({
        photoSetId: metadata.photoSetId, batteryId: battery.id, module: metadata.module,
        mimeType, byteSize: imageData.length, width: metadata.width, height: metadata.height,
        imageData, uploadedByUserId: request.actor!.userId
      }).onConflictDoUpdate({
        target: [checkerImages.photoSetId, checkerImages.module],
        set: { mimeType, byteSize: imageData.length, width: metadata.width, height: metadata.height, imageData, uploadedByUserId: request.actor!.userId, uploadedAt: new Date() }
      }).returning();
      return image;
    });
    return reply.status(201).send({
      id: saved.id, batteryId: saved.batteryId, photoSetId: saved.photoSetId, module: saved.module,
      mimeType: saved.mimeType, byteSize: saved.byteSize, width: saved.width, height: saved.height,
      uploadedAt: iso(saved.uploadedAt), recognition
    });
  });

  app.post<{ Params: { id: string } }>("/api/batteries/:id/checker-preview", async request => {
    const data = checkerPreviewInputSchema.parse(request.body);
    const battery = await requireBattery(request.params.id, request.actor!);
    if (battery.cellCount !== 12) throw Object.assign(new Error("Checker A/B recognition is available only for 12-cell battery types"), { statusCode: 400 });
    const [photoSet] = await db.select().from(checkerPhotoSets).where(and(eq(checkerPhotoSets.id, data.photoSetId), eq(checkerPhotoSets.batteryId, battery.id)));
    if (!photoSet) throw Object.assign(new Error("Checker photo set does not belong to this battery"), { statusCode: 400 });
    const [photoCount] = await db.select({ count: sql<number>`count(*)::int` }).from(checkerImages).where(eq(checkerImages.photoSetId, data.photoSetId));
    if (photoCount.count !== 2) throw Object.assign(new Error("Both checker photos A and B are required"), { statusCode: 400 });
    const [configuration] = await db.select().from(settings).where(eq(settings.id, 1));
    return combineCheckerReadings(data.photoSetId, data.A.cells, data.B.cells, Number(configuration.warningCellDeltaV), Number(configuration.dangerCellDeltaV), battery.minVoltage, battery.maxVoltage);
  });

  app.post<{ Params: { id: string } }>("/api/batteries/:id/cycles", async (request, reply) => {
    assertGroupAdministrator(request.actor);
    const data = cycleEventInputSchema.parse(request.body);
    await requireBattery(request.params.id, request.actor!);
    const [event] = await db.insert(cycleEvents).values({ batteryId: request.params.id, ...data }).returning();
    return reply.status(201).send({ ...event, occurredAt: iso(event.occurredAt) });
  });

  app.patch<{ Params: { id: string } }>("/api/admin/measurements/:id", async (request) => {
    assertGroupAdministrator(request.actor);
    const data = measurementCorrectionSchema.parse(request.body);
    const [current] = await db.select().from(measurements).where(eq(measurements.id, request.params.id));
    if (!current) throw Object.assign(new Error("Measurement not found"), { statusCode: 404 });
    const cells = data.cellVoltages ?? current.cellVoltages;
    const battery = await requireBattery(current.batteryId, request.actor!);
    if (cells.length !== battery.cellCount) throw Object.assign(new Error(`Expected ${battery.cellCount} cell voltages, received ${cells.length}`), { statusCode: 400 });
    const warning = Number(current.warningThresholdV);
    const danger = Number(current.dangerThresholdV);
    const result = calculateCellHealth(cells, warning, danger);
    const totalVoltage = sumCellVoltages(cells);
    const chargePercent = calculateChargePercent(totalVoltage, battery.minVoltage, battery.maxVoltage);
    const [updated] = await db.update(measurements).set({
      totalVoltage: totalVoltage.toString(), cellVoltages: data.cellVoltages,
      minCellVoltage: result.minCellVoltage.toString(), maxCellVoltage: result.maxCellVoltage.toString(),
      cellDelta: result.cellDelta.toString(), health: result.health, chargePercent,
      notes: data.notes,
      correctedAt: new Date(), correctedByUserId: request.actor!.userId
    }).where(eq(measurements.id, current.id)).returning();
    const [configuration] = await db.select().from(settings).where(eq(settings.id, 1));
    await rebuildCycleHistory(battery.id, { chargedThresholdPercent: configuration.chargedThresholdPercent, dischargedThresholdPercent: configuration.dischargedThresholdPercent });
    return mapMeasurement(updated);
  });

  app.post<{ Params: { id: string } }>("/api/admin/batteries/:id/archive", async request => {
    assertGroupAdministrator(request.actor);
    const battery = await requireBattery(request.params.id, request.actor!);
    const [updated] = await db.update(batteries).set({ state: "retired", archivedAt: new Date(), updatedAt: new Date() }).where(eq(batteries.id, battery.id)).returning();
    return { ...updated, createdAt: iso(updated.createdAt), updatedAt: iso(updated.updatedAt), archivedAt: updated.archivedAt ? iso(updated.archivedAt) : null };
  });

  app.post<{ Params: { id: string } }>("/api/admin/batteries/:id/restore", async request => {
    assertGroupAdministrator(request.actor);
    const battery = await requireBattery(request.params.id, request.actor!);
    const [updated] = await db.update(batteries).set({ state: "storage", archivedAt: null, updatedAt: new Date() }).where(eq(batteries.id, battery.id)).returning();
    return { ...updated, createdAt: iso(updated.createdAt), updatedAt: iso(updated.updatedAt), archivedAt: null };
  });

  app.get("/api/settings/thresholds", async () => {
    const [row] = await db.select().from(settings).where(eq(settings.id, 1));
    return { warningCellDeltaV: Number(row.warningCellDeltaV), dangerCellDeltaV: Number(row.dangerCellDeltaV), chargedThresholdPercent: row.chargedThresholdPercent, dischargedThresholdPercent: row.dischargedThresholdPercent };
  });

  app.put("/api/settings/thresholds", async request => {
    assertSuperAdmin(request.actor);
    const data = thresholdInputSchema.parse(request.body);
    const [row] = await db.update(settings).set({ warningCellDeltaV: data.warningCellDeltaV.toString(), dangerCellDeltaV: data.dangerCellDeltaV.toString(), chargedThresholdPercent: data.chargedThresholdPercent, dischargedThresholdPercent: data.dischargedThresholdPercent, updatedAt: new Date() }).where(eq(settings.id, 1)).returning();
    const batteryRows = await db.select({ id: batteries.id }).from(batteries);
    for (const battery of batteryRows) await rebuildCycleHistory(battery.id, { chargedThresholdPercent: row.chargedThresholdPercent, dischargedThresholdPercent: row.dischargedThresholdPercent });
    return { warningCellDeltaV: Number(row.warningCellDeltaV), dangerCellDeltaV: Number(row.dangerCellDeltaV), chargedThresholdPercent: row.chargedThresholdPercent, dischargedThresholdPercent: row.dischargedThresholdPercent };
  });

  return app;
}
