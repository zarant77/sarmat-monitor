import type { FastifyInstance } from "fastify";
import { and, asc, eq, gt, isNull, lte, ne, or, sql } from "drizzle-orm";
import { z } from "zod";
import { db } from "./db/index.js";
import { batteries, batteryTypes, measurements, batteryTelemetrySessions, batteryVoltageEvents, crews, droneFlightEvents, droneFlights, drones, flightMotors, groups, motorInstallations } from "./db/schema.js";
import { assertBatteryOperational } from "./battery-lifecycle.js";
import { currentBatteryCharge } from "./current-charge.js";

const selectionSchema = z.object({
  batteryId: z.uuid(), droneId: z.uuid().optional(), sessionId: z.uuid().optional(), expectedActiveId: z.uuid().nullable(),
  expectedActiveSince: z.iso.datetime({ offset: true }).nullable()
});
const flightEventSchema = z.object({
  id: z.uuid(), flightId: z.uuid(), sessionId: z.uuid(), type: z.enum(["armed", "disarmed"]),
  occurredAt: z.iso.datetime({ offset: true })
});
const voltageSchema = z.object({
  id: z.uuid(), sessionId: z.uuid(), type: z.enum(["vehicle_connected", "vehicle_disarmed", "consumption_sample"]),
  totalVoltage: z.number().finite().positive().max(1000).transform(value => Math.round(value * 1000) / 1000).pipe(z.number().positive()),
  currentAmps: z.number().finite().min(0).max(10000).transform(value => Math.round(value * 1000) / 1000).nullable().optional(),
  consumedMah: z.number().finite().min(0).max(10_000_000_000).transform(value => Math.round(value * 1000) / 1000).nullable().optional(),
  consumptionComplete: z.boolean().default(false),
  armed: z.boolean().nullable().optional(),
  occurredAt: z.iso.datetime({ offset: true }), measuredAt: z.iso.datetime({ offset: true })
}).refine(value => value.type !== "consumption_sample" || value.consumedMah != null, "Consumption samples require cumulative mAh");

export async function registerStationBatteries(app: FastifyInstance) {
  // Separate from user-session routes: the station secret only grants crew-local access.
  await app.register(async station => {
    station.addHook("preValidation", async request => {
      const secret = /^Bearer\s+(.+)$/i.exec(request.headers.authorization ?? "")?.[1]?.trim();
      const [row] = secret ? await db.select({ crew: crews, group: groups }).from(crews)
        .innerJoin(groups, eq(crews.groupId, groups.id)).where(eq(crews.secret, secret)) : [];
      if (!row || !row.crew.enabled || !row.group.enabled)
        throw Object.assign(new Error("Unauthorized"), { statusCode: 401 });
      request.telemetryCrew = row.crew;
    });
    station.get("/station/batteries", async (request, reply) => {
      reply.header("Cache-Control", "no-store");
      const rows = await db.select({ id: batteries.id, label: batteries.label,
        serialNumber: batteries.serialNumber, activeSince: batteries.activeSince }).from(batteries)
        .where(and(eq(batteries.crewId, request.telemetryCrew!.id), isNull(batteries.archivedAt), ne(batteries.state, "retired")))
        .orderBy(asc(batteries.label), asc(batteries.id));
      const active = rows.find(row => row.activeSince !== null);
      const availableDrones = await db.select({ id: drones.id, name: drones.name, model: drones.model, motorCount: drones.motorCount })
        .from(drones).where(and(eq(drones.crewId, request.telemetryCrew!.id), isNull(drones.retiredAt))).orderBy(asc(drones.name));
      return { batteries: rows, drones: availableDrones, activeBatteryId: active?.id ?? null, activeSince: active?.activeSince ?? null };
    });
    station.get<{ Params: { id: string } }>("/station/batteries/sessions/:id/charge", async (request, reply) => {
      const sessionId = z.uuid().parse(request.params.id);
      reply.header("Cache-Control", "no-store");
      const [row] = await db.select({ battery: batteries, type: batteryTypes }).from(batteryTelemetrySessions)
        .innerJoin(batteries, eq(batteryTelemetrySessions.batteryId, batteries.id))
        .innerJoin(batteryTypes, eq(batteries.typeId, batteryTypes.id))
        .where(and(eq(batteryTelemetrySessions.id, sessionId), eq(batteryTelemetrySessions.crewId, request.telemetryCrew!.id), eq(batteries.crewId, request.telemetryCrew!.id)));
      if (!row) throw Object.assign(new Error("Battery session not found"), { statusCode: 404 });
      const measurementRows = await db.select().from(measurements).where(eq(measurements.batteryId, row.battery.id));
      const voltageRows = await db.select().from(batteryVoltageEvents).where(eq(batteryVoltageEvents.batteryId, row.battery.id));
      const resistance = row.battery.internalResistanceMilliOhmsOverride ?? row.type.internalResistanceMilliOhms;
      return { sessionId, currentCharge: currentBatteryCharge(measurementRows, voltageRows, {
        minVoltage: Number(row.type.minVoltage), maxVoltage: Number(row.type.maxVoltage),
        capacityAh: Number(row.battery.actualCapacityAh ?? row.type.capacityAh),
        internalResistanceMilliOhms: resistance == null ? null : Number(resistance)
      }) };
    });
    station.put("/station/batteries/active", async request => {
      const data = selectionSchema.parse(request.body);
      const crewId = request.telemetryCrew!.id;
      return db.transaction(async tx => {
        await tx.execute(sql`select pg_advisory_xact_lock(hashtext(${crewId}))`);
        const [target] = await tx.select().from(batteries)
          .where(and(eq(batteries.id, data.batteryId), eq(batteries.crewId, crewId))).for("update");
        if (!target) throw Object.assign(new Error("Battery not found"), { statusCode: 404 });
        assertBatteryOperational(target);
        if (data.droneId) {
          const [targetDrone] = await tx.select({ id: drones.id }).from(drones)
            .where(and(eq(drones.id, data.droneId), eq(drones.crewId, crewId), isNull(drones.retiredAt)));
          if (!targetDrone) throw Object.assign(new Error("Drone not found"), { statusCode: 404 });
        }
        if (data.sessionId) {
          const [previous] = await tx.select().from(batteryTelemetrySessions).where(eq(batteryTelemetrySessions.id, data.sessionId));
          if (previous && (previous.batteryId !== target.id || previous.crewId !== crewId || (previous.droneId && previous.droneId !== data.droneId)))
            throw Object.assign(new Error("Session already bound to other equipment"), { statusCode: 409 });
          if (previous && !previous.droneId && data.droneId)
            await tx.update(batteryTelemetrySessions).set({ droneId: data.droneId }).where(eq(batteryTelemetrySessions.id, previous.id));
        }
        const [active] = await tx.select().from(batteries)
          .where(and(eq(batteries.crewId, crewId), sql`${batteries.activeSince} is not null`));
        if ((active?.id ?? null) !== data.expectedActiveId ||
            (active?.activeSince?.getTime() ?? null) !== (data.expectedActiveSince ? new Date(data.expectedActiveSince).getTime() : null))
          throw Object.assign(new Error("Active battery changed. Reload and select again."), { statusCode: 409 });
        // Selecting the existing battery is idempotent, never a toggle.
        if (data.sessionId) {
          await tx.insert(batteryTelemetrySessions).values({ id: data.sessionId, batteryId: target.id, crewId, droneId: data.droneId }).onConflictDoNothing();
          const [bound] = await tx.select().from(batteryTelemetrySessions).where(eq(batteryTelemetrySessions.id, data.sessionId));
          if (bound.batteryId !== target.id || bound.crewId !== crewId || (data.droneId && bound.droneId !== data.droneId))
            throw Object.assign(new Error("Session already bound to other equipment"), { statusCode: 409 });
        }
        if (active?.id === target.id) return { activeBatteryId: active.id, activeSince: active.activeSince, sessionId: data.sessionId, droneId: data.droneId };
        await tx.update(batteries).set({ activeSince: null }).where(eq(batteries.crewId, crewId));
        const activeSince = new Date();
        await tx.update(batteries).set({ activeSince, lastActiveSince: activeSince }).where(eq(batteries.id, target.id));
        return { activeBatteryId: target.id, activeSince, sessionId: data.sessionId, droneId: data.droneId };
      });
    });
    station.post("/station/batteries/voltage-events", async (request, reply) => {
      const data = voltageSchema.parse(request.body);
      const occurredAt = new Date(data.occurredAt), measuredAt = new Date(data.measuredAt);
      // Samples are taken after the trigger, with a bounded wait for a fresh SYS_STATUS.
      if (measuredAt.getTime() < occurredAt.getTime() || measuredAt.getTime() - occurredAt.getTime() > 30_000 || measuredAt.getTime() > Date.now() + 300_000)
        throw Object.assign(new Error("Invalid voltage sample time"), { statusCode: 400 });
      const result = await db.transaction(async tx => {
        const [session] = await tx.select().from(batteryTelemetrySessions)
          .where(and(eq(batteryTelemetrySessions.id, data.sessionId), eq(batteryTelemetrySessions.crewId, request.telemetryCrew!.id)));
        if (!session) throw Object.assign(new Error("Battery session not found"), { statusCode: 404 });
        // Resolve the immutable selection, never the active battery at delivery time.
        const currentAmps = data.currentAmps ?? null;
        const consumedMah = data.consumedMah ?? null;
        const values = { ...data, batteryId: session.batteryId, totalVoltage: data.totalVoltage.toFixed(3), currentAmps: currentAmps?.toFixed(3) ?? null,
          consumedMah: consumedMah?.toFixed(3) ?? null, armed: data.armed ?? null, occurredAt, measuredAt };
        const [inserted] = await tx.insert(batteryVoltageEvents).values(values).onConflictDoNothing().returning();
        if (inserted) return { id: inserted.id, duplicate: false };
        const [existing] = await tx.select().from(batteryVoltageEvents).where(eq(batteryVoltageEvents.id, data.id));
        if (!existing || existing.sessionId !== data.sessionId || existing.type !== data.type || Number(existing.totalVoltage) !== data.totalVoltage ||
            (existing.currentAmps === null ? null : Number(existing.currentAmps)) !== currentAmps ||
            (existing.consumedMah === null ? null : Number(existing.consumedMah)) !== consumedMah || existing.consumptionComplete !== data.consumptionComplete ||
            existing.armed !== (data.armed ?? null) ||
            existing.occurredAt.getTime() !== occurredAt.getTime() || existing.measuredAt.getTime() !== measuredAt.getTime())
          throw Object.assign(new Error("Event ID already used with different data"), { statusCode: 409 });
        return { id: existing.id, duplicate: true };
      });
      return reply.status(result.duplicate ? 200 : 201).send(result);
    });

    station.post("/station/flights/events", async (request, reply) => {
      const data = flightEventSchema.parse(request.body);
      const occurredAt = new Date(data.occurredAt);
      if (occurredAt.getTime() > Date.now() + 300_000) throw Object.assign(new Error("Invalid flight event time"), { statusCode: 400 });
      const result = await db.transaction(async tx => {
        await tx.execute(sql`select pg_advisory_xact_lock(hashtext(${data.flightId}))`);
        const [session] = await tx.select().from(batteryTelemetrySessions)
          .where(and(eq(batteryTelemetrySessions.id, data.sessionId), eq(batteryTelemetrySessions.crewId, request.telemetryCrew!.id)));
        if (!session?.droneId) throw Object.assign(new Error("Drone session not found"), { statusCode: 404 });
        const [existingEvent] = await tx.select().from(droneFlightEvents).where(eq(droneFlightEvents.id, data.id));
        if (existingEvent) {
          if (existingEvent.flightId !== data.flightId || existingEvent.telemetrySessionId !== data.sessionId || existingEvent.type !== data.type || existingEvent.occurredAt.getTime() !== occurredAt.getTime())
            throw Object.assign(new Error("Event ID already used with different data"), { statusCode: 409 });
          return { id: existingEvent.id, flightId: data.flightId, duplicate: true };
        }

        if (data.type === "armed") {
          const [existingFlight] = await tx.select().from(droneFlights).where(eq(droneFlights.id, data.flightId));
          if (existingFlight) {
            if (existingFlight.telemetrySessionId !== data.sessionId || existingFlight.droneId !== session.droneId || existingFlight.armedAt.getTime() !== occurredAt.getTime())
              throw Object.assign(new Error("Flight ID already used with different data"), { statusCode: 409 });
          } else {
            await tx.insert(droneFlights).values({ id: data.flightId, telemetrySessionId: data.sessionId, droneId: session.droneId, armedAt: occurredAt });
            const installed = await tx.select().from(motorInstallations).where(and(
              eq(motorInstallations.droneId, session.droneId), lte(motorInstallations.installedAt, occurredAt),
              or(isNull(motorInstallations.removedAt), gt(motorInstallations.removedAt, occurredAt))
            ));
            if (installed.length) await tx.insert(flightMotors).values(installed.map(item => ({ flightId: data.flightId, motorId: item.motorId, installationId: item.id, positionNumber: item.positionNumber })));
          }
        } else {
          const [flight] = await tx.select().from(droneFlights).where(and(eq(droneFlights.id, data.flightId), eq(droneFlights.telemetrySessionId, data.sessionId))).for("update");
          if (!flight) throw Object.assign(new Error("Armed event not found"), { statusCode: 409 });
          const durationSeconds = Math.floor((occurredAt.getTime() - flight.armedAt.getTime()) / 1000);
          if (durationSeconds < 0 || durationSeconds > 172_800) throw Object.assign(new Error("Invalid flight duration"), { statusCode: 400 });
          if (flight.disarmedAt && flight.disarmedAt.getTime() !== occurredAt.getTime()) throw Object.assign(new Error("Flight already completed with different data"), { statusCode: 409 });
          if (!flight.disarmedAt) await tx.update(droneFlights).set({ disarmedAt: occurredAt, durationSeconds }).where(eq(droneFlights.id, flight.id));
        }
        await tx.insert(droneFlightEvents).values({ id: data.id, flightId: data.flightId, telemetrySessionId: data.sessionId, type: data.type, occurredAt });
        return { id: data.id, flightId: data.flightId, duplicate: false };
      });
      return reply.status(result.duplicate ? 200 : 201).send(result);
    });
  });
}
