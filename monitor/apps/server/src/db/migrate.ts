import { migrate } from "drizzle-orm/postgres-js/migrator";
import { fileURLToPath } from "node:url";
import { asc, eq, sql } from "drizzle-orm";
import { connection, db } from "./index.js";
import { batteries, settings } from "./schema.js";
import { rebuildInferredCycleEvents } from "../cycle-inference.js";

try {
  await migrate(db, { migrationsFolder: fileURLToPath(new URL("../../drizzle", import.meta.url)) });
  // Rebuild from primary measurements, including databases whose limits were
  // changed before dynamic percentages were introduced. Safe to retry on failure.
  await db.transaction(async tx => {
    await tx.execute(sql`lock table settings, battery_types, batteries, measurements, cycle_events in share row exclusive mode`);
    const [configuration] = await tx.select().from(settings).where(eq(settings.id, 1));
    if (!configuration) throw new Error("Charge settings are missing; cannot rebuild battery history");
    const rows = await tx.select({ id: batteries.id }).from(batteries).orderBy(asc(batteries.id));
    for (const battery of rows) await rebuildInferredCycleEvents(battery.id, configuration, tx);
  });
  console.log("Database migrations and inferred battery history rebuild complete.");
} finally {
  await connection.end();
}
