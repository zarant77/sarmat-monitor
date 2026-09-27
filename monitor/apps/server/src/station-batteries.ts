import type { FastifyInstance } from "fastify";
import { and, asc, eq, isNull, ne, sql } from "drizzle-orm";
import { z } from "zod";
import { db } from "./db/index.js";
import { batteries, batteryTelemetrySessions, batteryVoltageEvents, crews, groups } from "./db/schema.js";
import { assertBatteryOperational } from "./battery-lifecycle.js";

const selectionSchema = z.object({
  batteryId: z.uuid(), sessionId: z.uuid().optional(), expectedActiveId: z.uuid().nullable(),
  expectedActiveSince: z.iso.datetime({ offset: true }).nullable()
});
const voltageSchema = z.object({
  id: z.uuid(), sessionId: z.uuid(), type: z.enum(["vehicle_connected", "vehicle_disarmed"]),
  totalVoltage: z.number().finite().positive().max(1000).transform(value => Math.round(value * 1000) / 1000).pipe(z.number().positive()),
  occurredAt: z.iso.datetime({ offset: true }), measuredAt: z.iso.datetime({ offset: true })
});

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
      return { batteries: rows, activeBatteryId: active?.id ?? null, activeSince: active?.activeSince ?? null };
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
        if (data.sessionId) {
          const [previous] = await tx.select().from(batteryTelemetrySessions).where(eq(batteryTelemetrySessions.id, data.sessionId));
          if (previous && (previous.batteryId !== target.id || previous.crewId !== crewId))
            throw Object.assign(new Error("Session already bound to another battery"), { statusCode: 409 });
        }
        const [active] = await tx.select().from(batteries)
          .where(and(eq(batteries.crewId, crewId), sql`${batteries.activeSince} is not null`));
        if ((active?.id ?? null) !== data.expectedActiveId ||
            (active?.activeSince?.getTime() ?? null) !== (data.expectedActiveSince ? new Date(data.expectedActiveSince).getTime() : null))
          throw Object.assign(new Error("Active battery changed. Reload and select again."), { statusCode: 409 });
        // Selecting the existing battery is idempotent, never a toggle.
        if (data.sessionId) {
          await tx.insert(batteryTelemetrySessions).values({ id: data.sessionId, batteryId: target.id, crewId }).onConflictDoNothing();
          const [bound] = await tx.select().from(batteryTelemetrySessions).where(eq(batteryTelemetrySessions.id, data.sessionId));
          if (bound.batteryId !== target.id || bound.crewId !== crewId)
            throw Object.assign(new Error("Session already bound to another battery"), { statusCode: 409 });
        }
        if (active?.id === target.id) return { activeBatteryId: active.id, activeSince: active.activeSince, sessionId: data.sessionId };
        await tx.update(batteries).set({ activeSince: null }).where(eq(batteries.crewId, crewId));
        const activeSince = new Date();
        await tx.update(batteries).set({ activeSince }).where(eq(batteries.id, target.id));
        return { activeBatteryId: target.id, activeSince, sessionId: data.sessionId };
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
        const values = { ...data, batteryId: session.batteryId, totalVoltage: data.totalVoltage.toFixed(3), occurredAt, measuredAt };
        const [inserted] = await tx.insert(batteryVoltageEvents).values(values).onConflictDoNothing().returning();
        if (inserted) return { id: inserted.id, duplicate: false };
        const [existing] = await tx.select().from(batteryVoltageEvents).where(eq(batteryVoltageEvents.id, data.id));
        if (!existing || existing.sessionId !== data.sessionId || existing.type !== data.type || Number(existing.totalVoltage) !== data.totalVoltage ||
            existing.occurredAt.getTime() !== occurredAt.getTime() || existing.measuredAt.getTime() !== measuredAt.getTime())
          throw Object.assign(new Error("Event ID already used with different data"), { statusCode: 409 });
        return { id: existing.id, duplicate: true };
      });
      return reply.status(result.duplicate ? 200 : 201).send(result);
    });
  });
}
