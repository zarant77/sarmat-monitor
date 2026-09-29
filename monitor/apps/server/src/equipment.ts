import type { FastifyInstance } from "fastify";
import { and, asc, desc, eq, gt, ilike, inArray, isNotNull, isNull, lte, or, sql } from "drizzle-orm";
import { alias } from "drizzle-orm/pg-core";
import {
  droneInputSchema, droneUpdateSchema, flightCorrectionSchema, motorAssignmentSchema, motorInputSchema,
  motorRemovalSchema, motorUpdateSchema
} from "@sbm/shared";
import { assertGroupAccess, assertGroupAdministrator, type Actor } from "./auth.js";
import { db } from "./db/index.js";
import { crews, droneFlights, drones, flightCorrections, flightMotors, groups, motorInstallations, motors, users } from "./db/schema.js";

const iso = (value: Date) => value.toISOString();
const installedUsers = alias(users, "installed_users");
const removedUsers = alias(users, "removed_users");
const correctionUsers = alias(users, "correction_users");
const activeMotorCount = sql<number>`(select count(*)::int from ${motorInstallations} where ${motorInstallations.droneId} = ${drones.id} and ${motorInstallations.removedAt} is null)`;
const droneFlightSeconds = sql<number>`(select coalesce(sum(${droneFlights.durationSeconds}), 0)::int from ${droneFlights} where ${droneFlights.droneId} = ${drones.id} and ${droneFlights.durationSeconds} is not null and ${droneFlights.excludedAt} is null)`;
const openFlightStartedAt = sql<Date | null>`(select ${droneFlights.armedAt} from ${droneFlights} where ${droneFlights.droneId} = ${drones.id} and ${droneFlights.disarmedAt} is null and ${droneFlights.excludedAt} is null order by ${droneFlights.armedAt} desc limit 1)`;
const motorFlightSeconds = sql<number>`(select coalesce(sum(df.duration_seconds), 0)::int from ${flightMotors} fm join ${droneFlights} df on df.id = fm.flight_id where fm.motor_id = ${motors.id} and df.duration_seconds is not null and df.excluded_at is null)`;

function mapDrone(row: { drone: typeof drones.$inferSelect; crew: typeof crews.$inferSelect; group: typeof groups.$inferSelect; installedMotorCount?: number; flightSeconds?: number; openFlightStartedAt?: Date | string | null }) {
  const { drone, crew, group } = row;
  return {
    ...drone, groupId: group.id, groupName: group.name, crewNumber: crew.number, crewName: crew.name,
    installedMotorCount: Number(row.installedMotorCount ?? 0), totalFlightSeconds: drone.initialFlightSeconds + Number(row.flightSeconds ?? 0),
    openFlightStartedAt: row.openFlightStartedAt ? new Date(row.openFlightStartedAt).toISOString() : null,
    status: drone.retiredAt ? "retired" as const : "active" as const,
    retiredAt: drone.retiredAt ? iso(drone.retiredAt) : null, createdAt: iso(drone.createdAt), updatedAt: iso(drone.updatedAt)
  };
}

type MotorRow = { motor: typeof motors.$inferSelect; group: typeof groups.$inferSelect; installation?: typeof motorInstallations.$inferSelect | null; drone?: typeof drones.$inferSelect | null; flightSeconds?: number };
function mapMotor(row: MotorRow) {
  const { motor, group, installation, drone } = row;
  return {
    ...motor, groupName: group.name, totalFlightSeconds: motor.initialFlightSeconds + Number(row.flightSeconds ?? 0),
    currentDroneId: drone?.id ?? null, currentDroneName: drone?.name ?? null, positionNumber: installation?.positionNumber ?? null,
    status: motor.retiredAt ? "retired" as const : installation ? "installed" as const : "stock" as const,
    retiredAt: motor.retiredAt ? iso(motor.retiredAt) : null, createdAt: iso(motor.createdAt), updatedAt: iso(motor.updatedAt)
  };
}

async function requireDrone(id: string, actor: Actor) {
  const [row] = await db.select({ drone: drones, crew: crews, group: groups, installedMotorCount: activeMotorCount, flightSeconds: droneFlightSeconds, openFlightStartedAt }).from(drones)
    .innerJoin(crews, eq(drones.crewId, crews.id)).innerJoin(groups, eq(crews.groupId, groups.id)).where(eq(drones.id, id));
  if (!row) throw Object.assign(new Error("Drone not found"), { statusCode: 404 });
  assertGroupAccess(actor, row.group.id);
  return row;
}

async function requireMotor(id: string, actor: Actor) {
  const [row] = await db.select({ motor: motors, group: groups, installation: motorInstallations, drone: drones, flightSeconds: motorFlightSeconds }).from(motors)
    .innerJoin(groups, eq(motors.groupId, groups.id))
    .leftJoin(motorInstallations, and(eq(motorInstallations.motorId, motors.id), isNull(motorInstallations.removedAt)))
    .leftJoin(drones, eq(motorInstallations.droneId, drones.id)).where(eq(motors.id, id));
  if (!row) throw Object.assign(new Error("Motor not found"), { statusCode: 404 });
  assertGroupAccess(actor, row.group.id);
  return row;
}

async function requireCrew(id: string, actor: Actor) {
  const [row] = await db.select({ crew: crews, group: groups }).from(crews).innerJoin(groups, eq(crews.groupId, groups.id)).where(eq(crews.id, id));
  if (!row) throw Object.assign(new Error("Crew not found"), { statusCode: 404 });
  assertGroupAccess(actor, row.group.id);
  if (!row.crew.enabled || !row.group.enabled) throw Object.assign(new Error("Assigned crew is not available"), { statusCode: 400 });
  return row;
}

async function installationHistory(where: ReturnType<typeof eq>) {
  const rows = await db.select({ installation: motorInstallations, motor: motors, drone: drones, installedByUsername: installedUsers.username, removedByUsername: removedUsers.username })
    .from(motorInstallations).innerJoin(motors, eq(motorInstallations.motorId, motors.id)).innerJoin(drones, eq(motorInstallations.droneId, drones.id))
    .leftJoin(installedUsers, eq(motorInstallations.installedByUserId, installedUsers.id)).leftJoin(removedUsers, eq(motorInstallations.removedByUserId, removedUsers.id))
    .where(where).orderBy(desc(motorInstallations.installedAt));
  return rows.map(({ installation, motor, drone, installedByUsername, removedByUsername }) => ({
    id: installation.id, motorId: motor.id, serialNumber: motor.serialNumber, droneId: drone.id, droneName: drone.name,
    positionNumber: installation.positionNumber, installedAt: iso(installation.installedAt), removedAt: installation.removedAt ? iso(installation.removedAt) : null,
    installedByUsername, removedByUsername, installNotes: installation.installNotes, removalNotes: installation.removalNotes, active: !installation.removedAt
  }));
}

async function droneDetail(id: string, actor: Actor) {
  const base = await requireDrone(id, actor);
  const history = await installationHistory(eq(motorInstallations.droneId, id));
  const active = new Map(history.filter(item => item.active).map(item => [item.positionNumber, item]));
  return { ...mapDrone(base), slots: Array.from({ length: base.drone.motorCount }, (_, index) => ({ positionNumber: index + 1, installation: active.get(index + 1) ?? null })), installationHistory: history, flights: await flightHistory(id) };
}

async function motorDetail(id: string, actor: Actor) {
  const base = await requireMotor(id, actor);
  return { ...mapMotor(base), installationHistory: await installationHistory(eq(motorInstallations.motorId, id)) };
}

async function flightHistory(droneId: string) {
  const flights = await db.select().from(droneFlights).where(eq(droneFlights.droneId, droneId)).orderBy(desc(droneFlights.armedAt));
  if (!flights.length) return [];
  const snapshots = await db.select({ flightId: flightMotors.flightId, motorId: motors.id, serialNumber: motors.serialNumber, positionNumber: flightMotors.positionNumber })
    .from(flightMotors).innerJoin(motors, eq(flightMotors.motorId, motors.id)).where(inArray(flightMotors.flightId, flights.map(item => item.id))).orderBy(asc(flightMotors.positionNumber));
  const corrections = await db.select({ correction: flightCorrections, correctedByUsername: correctionUsers.username }).from(flightCorrections)
    .leftJoin(correctionUsers, eq(flightCorrections.correctedByUserId, correctionUsers.id))
    .where(inArray(flightCorrections.flightId, flights.map(item => item.id))).orderBy(desc(flightCorrections.correctedAt));
  return flights.map(flight => ({
    id: flight.id, droneId: flight.droneId, armedAt: iso(flight.armedAt), disarmedAt: flight.disarmedAt ? iso(flight.disarmedAt) : null,
    durationSeconds: flight.durationSeconds, excludedAt: flight.excludedAt ? iso(flight.excludedAt) : null,
    status: flight.excludedAt ? "excluded" as const : flight.disarmedAt ? "completed" as const : "armed" as const,
    motors: snapshots.filter(item => item.flightId === flight.id).map(({ flightId: _flightId, ...motor }) => motor),
    corrections: corrections.filter(item => item.correction.flightId === flight.id).map(({ correction, correctedByUsername }) => ({
      id: correction.id, previousArmedAt: iso(correction.previousArmedAt), previousDisarmedAt: correction.previousDisarmedAt ? iso(correction.previousDisarmedAt) : null,
      newArmedAt: iso(correction.newArmedAt), newDisarmedAt: correction.newDisarmedAt ? iso(correction.newDisarmedAt) : null,
      previousExcluded: Boolean(correction.previousExcludedAt), newExcluded: Boolean(correction.newExcludedAt), notes: correction.notes,
      correctedByUsername, correctedAt: iso(correction.correctedAt)
    }))
  }));
}

async function requireFlight(id: string, actor: Actor) {
  const [row] = await db.select({ flight: droneFlights, drone: drones, crew: crews, group: groups }).from(droneFlights)
    .innerJoin(drones, eq(droneFlights.droneId, drones.id)).innerJoin(crews, eq(drones.crewId, crews.id)).innerJoin(groups, eq(crews.groupId, groups.id))
    .where(eq(droneFlights.id, id));
  if (!row) throw Object.assign(new Error("Flight not found"), { statusCode: 404 });
  assertGroupAccess(actor, row.group.id);
  return row;
}

function parsePosition(value: string) {
  const position = Number(value);
  if (!Number.isInteger(position) || position < 1) throw Object.assign(new Error("Invalid motor position"), { statusCode: 400 });
  return position;
}

export async function registerEquipment(app: FastifyInstance): Promise<void> {
  app.get<{ Querystring: { groupId?: string; crewId?: string; includeRetired?: string } }>("/api/drones", async request => {
    assertGroupAdministrator(request.actor); const actor = request.actor!;
    const groupId = actor.role === "SUPER_ADMIN" ? request.query.groupId : actor.groupId!;
    if (groupId) assertGroupAccess(actor, groupId);
    const rows = await db.select({ drone: drones, crew: crews, group: groups, installedMotorCount: activeMotorCount, flightSeconds: droneFlightSeconds, openFlightStartedAt }).from(drones)
      .innerJoin(crews, eq(drones.crewId, crews.id)).innerJoin(groups, eq(crews.groupId, groups.id))
      .where(and(groupId ? eq(groups.id, groupId) : undefined, request.query.crewId ? eq(drones.crewId, request.query.crewId) : undefined,
        request.query.includeRetired === "true" ? undefined : isNull(drones.retiredAt))).orderBy(asc(crews.number), asc(drones.name));
    return rows.map(mapDrone);
  });

  app.get<{ Params: { id: string } }>("/api/drones/:id", async request => { assertGroupAdministrator(request.actor); return droneDetail(request.params.id, request.actor!); });

  app.post("/api/drones", async (request, reply) => {
    assertGroupAdministrator(request.actor); const data = droneInputSchema.parse(request.body); await requireCrew(data.crewId, request.actor!);
    const [drone] = await db.insert(drones).values(data).returning();
    return reply.status(201).send(mapDrone(await requireDrone(drone.id, request.actor!)));
  });

  app.patch<{ Params: { id: string } }>("/api/drones/:id", async request => {
    assertGroupAdministrator(request.actor); const actor = request.actor!; const current = await requireDrone(request.params.id, actor); const data = droneUpdateSchema.parse(request.body);
    if (data.motorCount !== undefined && data.motorCount !== current.drone.motorCount && current.installedMotorCount > 0) throw Object.assign(new Error("Remove all motors before changing the motor count"), { statusCode: 409 });
    if (data.crewId) {
      const target = await requireCrew(data.crewId, actor);
      if (target.group.id !== current.group.id) {
        const [history] = await db.select({ id: motorInstallations.id }).from(motorInstallations).where(eq(motorInstallations.droneId, current.drone.id)).limit(1);
        if (history) throw Object.assign(new Error("A drone with motor history cannot be moved to another group"), { statusCode: 409 });
      }
    }
    const [drone] = await db.update(drones).set({ ...data, updatedAt: new Date() }).where(eq(drones.id, current.drone.id)).returning();
    return mapDrone(await requireDrone(drone.id, actor));
  });

  app.post<{ Params: { id: string; position: string } }>("/api/drones/:id/motor-positions/:position", async request => {
    assertGroupAdministrator(request.actor); const actor = request.actor!; const position = parsePosition(request.params.position); const data = motorAssignmentSchema.parse(request.body);
    const drone = await requireDrone(request.params.id, actor); const motor = await requireMotor(data.motorId, actor);
    if (drone.group.id !== motor.group.id) throw Object.assign(new Error("Motor and drone must belong to the same group"), { statusCode: 400 });
    if (drone.drone.retiredAt) throw Object.assign(new Error("A retired drone cannot be equipped"), { statusCode: 409 });
    if (motor.motor.retiredAt) throw Object.assign(new Error("A retired motor cannot be installed"), { statusCode: 409 });
    if (position > drone.drone.motorCount) throw Object.assign(new Error("Motor position is outside the drone configuration"), { statusCode: 400 });
    await db.transaction(async tx => {
      const [lockedMotor] = await tx.select().from(motors).where(eq(motors.id, motor.motor.id)).for("update");
      const [alreadyInstalled] = await tx.select().from(motorInstallations).where(and(eq(motorInstallations.motorId, lockedMotor.id), isNull(motorInstallations.removedAt))).limit(1);
      if (alreadyInstalled) throw Object.assign(new Error("Motor is already installed"), { statusCode: 409 });
      const [current] = await tx.select().from(motorInstallations).where(and(eq(motorInstallations.droneId, drone.drone.id), eq(motorInstallations.positionNumber, position), isNull(motorInstallations.removedAt))).for("update");
      const now = new Date();
      if (current) await tx.update(motorInstallations).set({ removedAt: now, removedByUserId: actor.userId, removalNotes: data.notes }).where(eq(motorInstallations.id, current.id));
      await tx.insert(motorInstallations).values({ motorId: motor.motor.id, droneId: drone.drone.id, positionNumber: position, installedAt: now, installedByUserId: actor.userId, installNotes: data.notes });
    });
    return droneDetail(drone.drone.id, actor);
  });

  app.post<{ Params: { id: string; position: string } }>("/api/drones/:id/motor-positions/:position/remove", async request => {
    assertGroupAdministrator(request.actor); const actor = request.actor!; const position = parsePosition(request.params.position); const data = motorRemovalSchema.parse(request.body); const drone = await requireDrone(request.params.id, actor);
    if (position > drone.drone.motorCount) throw Object.assign(new Error("Motor position is outside the drone configuration"), { statusCode: 400 });
    const [removed] = await db.update(motorInstallations).set({ removedAt: new Date(), removedByUserId: actor.userId, removalNotes: data.notes })
      .where(and(eq(motorInstallations.droneId, drone.drone.id), eq(motorInstallations.positionNumber, position), isNull(motorInstallations.removedAt))).returning({ id: motorInstallations.id });
    if (!removed) throw Object.assign(new Error("Motor position is already empty"), { statusCode: 409 });
    return droneDetail(drone.drone.id, actor);
  });

  app.patch<{ Params: { id: string } }>("/api/admin/flights/:id", async request => {
    assertGroupAdministrator(request.actor); const actor = request.actor!; const accessible = await requireFlight(request.params.id, actor);
    const data = flightCorrectionSchema.parse(request.body);
    await db.transaction(async tx => {
      const [current] = await tx.select().from(droneFlights).where(eq(droneFlights.id, accessible.flight.id)).for("update");
      if (!current) throw Object.assign(new Error("Flight not found"), { statusCode: 404 });
      const armedAt = data.armedAt === undefined ? current.armedAt : new Date(data.armedAt);
      const disarmedAt = data.disarmedAt === undefined ? current.disarmedAt : data.disarmedAt === null ? null : new Date(data.disarmedAt);
      if (armedAt.getTime() > Date.now() + 300_000 || (disarmedAt && disarmedAt.getTime() > Date.now() + 300_000))
        throw Object.assign(new Error("Flight time cannot be in the future"), { statusCode: 400 });
      const durationSeconds = disarmedAt ? Math.floor((disarmedAt.getTime() - armedAt.getTime()) / 1000) : null;
      if (durationSeconds !== null && (durationSeconds < 0 || durationSeconds > 172_800))
        throw Object.assign(new Error("Invalid flight duration"), { statusCode: 400 });
      const excludedAt = data.excluded === undefined ? current.excludedAt : data.excluded ? current.excludedAt ?? new Date() : null;
      const excludedByUserId = excludedAt ? current.excludedAt ? current.excludedByUserId : actor.userId : null;
      await tx.update(droneFlights).set({ armedAt, disarmedAt, durationSeconds, excludedAt, excludedByUserId }).where(eq(droneFlights.id, current.id));
      if (armedAt.getTime() !== current.armedAt.getTime()) {
        await tx.delete(flightMotors).where(eq(flightMotors.flightId, current.id));
        const installed = await tx.select().from(motorInstallations).where(and(eq(motorInstallations.droneId, current.droneId),
          lte(motorInstallations.installedAt, armedAt), or(isNull(motorInstallations.removedAt), gt(motorInstallations.removedAt, armedAt))));
        if (installed.length) await tx.insert(flightMotors).values(installed.map(item => ({ flightId: current.id, motorId: item.motorId, installationId: item.id, positionNumber: item.positionNumber })));
      }
      await tx.insert(flightCorrections).values({ flightId: current.id, previousArmedAt: current.armedAt,
        previousDisarmedAt: current.disarmedAt, previousExcludedAt: current.excludedAt, newArmedAt: armedAt,
        newDisarmedAt: disarmedAt, newExcludedAt: excludedAt, notes: data.notes, correctedByUserId: actor.userId });
    });
    return droneDetail(accessible.drone.id, actor);
  });

  for (const action of ["retire", "restore"] as const) app.post<{ Params: { id: string } }>(`/api/admin/drones/:id/${action}`, async request => {
    assertGroupAdministrator(request.actor); const current = await requireDrone(request.params.id, request.actor!);
    if (action === "retire" && current.drone.retiredAt) throw Object.assign(new Error("Drone is already retired"), { statusCode: 409 });
    if (action === "retire" && current.installedMotorCount > 0) throw Object.assign(new Error("Remove all motors before retiring the drone"), { statusCode: 409 });
    if (action === "retire" && current.openFlightStartedAt) throw Object.assign(new Error("Complete or exclude the open flight before retiring the drone"), { statusCode: 409 });
    if (action === "restore" && !current.drone.retiredAt) throw Object.assign(new Error("Drone is not retired"), { statusCode: 409 });
    const [drone] = await db.update(drones).set({ retiredAt: action === "retire" ? new Date() : null, updatedAt: new Date() }).where(eq(drones.id, current.drone.id)).returning();
    return mapDrone(await requireDrone(drone.id, request.actor!));
  });

  app.get<{ Querystring: { groupId?: string; status?: "stock" | "installed" | "retired"; search?: string } }>("/api/motors", async request => {
    assertGroupAdministrator(request.actor); const actor = request.actor!; const groupId = actor.role === "SUPER_ADMIN" ? request.query.groupId : actor.groupId!;
    if (groupId) assertGroupAccess(actor, groupId);
    const statusFilter = request.query.status === "retired" ? isNotNull(motors.retiredAt) : request.query.status === "installed" ? and(isNull(motors.retiredAt), isNotNull(motorInstallations.id)) : request.query.status === "stock" ? and(isNull(motors.retiredAt), isNull(motorInstallations.id)) : undefined;
    const search = request.query.search?.trim();
    const rows = await db.select({ motor: motors, group: groups, installation: motorInstallations, drone: drones, flightSeconds: motorFlightSeconds }).from(motors).innerJoin(groups, eq(motors.groupId, groups.id))
      .leftJoin(motorInstallations, and(eq(motorInstallations.motorId, motors.id), isNull(motorInstallations.removedAt))).leftJoin(drones, eq(motorInstallations.droneId, drones.id))
      .where(and(groupId ? eq(motors.groupId, groupId) : undefined, statusFilter, search ? ilike(motors.serialNumber, `%${search}%`) : undefined)).orderBy(asc(motors.serialNumber));
    return rows.map(mapMotor);
  });

  app.get<{ Params: { id: string } }>("/api/motors/:id", async request => { assertGroupAdministrator(request.actor); return motorDetail(request.params.id, request.actor!); });

  app.post("/api/motors", async (request, reply) => {
    assertGroupAdministrator(request.actor); const actor = request.actor!; const data = motorInputSchema.parse(request.body); const groupId = actor.role === "SUPER_ADMIN" ? data.groupId : actor.groupId;
    if (!groupId) throw Object.assign(new Error("A group assignment is required"), { statusCode: 400 });
    assertGroupAccess(actor, groupId); const [group] = await db.select().from(groups).where(eq(groups.id, groupId));
    if (!group || !group.enabled) throw Object.assign(new Error("Assigned group is not available"), { statusCode: 400 });
    const { groupId: _groupId, ...values } = data; const [motor] = await db.insert(motors).values({ ...values, groupId }).returning();
    return reply.status(201).send(mapMotor(await requireMotor(motor.id, actor)));
  });

  app.patch<{ Params: { id: string } }>("/api/motors/:id", async request => {
    assertGroupAdministrator(request.actor); const current = await requireMotor(request.params.id, request.actor!); const data = motorUpdateSchema.parse(request.body);
    const [motor] = await db.update(motors).set({ ...data, updatedAt: new Date() }).where(eq(motors.id, current.motor.id)).returning();
    return mapMotor(await requireMotor(motor.id, request.actor!));
  });

  for (const action of ["retire", "restore"] as const) app.post<{ Params: { id: string } }>(`/api/admin/motors/:id/${action}`, async request => {
    assertGroupAdministrator(request.actor); const current = await requireMotor(request.params.id, request.actor!);
    if (action === "retire" && current.motor.retiredAt) throw Object.assign(new Error("Motor is already retired"), { statusCode: 409 });
    if (action === "retire" && current.installation) throw Object.assign(new Error("Remove the motor from the drone before retiring it"), { statusCode: 409 });
    if (action === "restore" && !current.motor.retiredAt) throw Object.assign(new Error("Motor is not retired"), { statusCode: 409 });
    const [motor] = await db.update(motors).set({ retiredAt: action === "retire" ? new Date() : null, updatedAt: new Date() }).where(eq(motors.id, current.motor.id)).returning();
    return mapMotor(await requireMotor(motor.id, request.actor!));
  });
}
