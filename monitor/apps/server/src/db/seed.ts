import { and, eq } from "drizzle-orm";
import { hash } from "bcryptjs";
import { db, connection } from "./index.js";
import { batteries, batteryTypes, crews, groups, transfers, users } from "./schema.js";

async function ensureGroup(name: string, code: string, notes: string) {
  const [existing] = await db.select().from(groups).where(eq(groups.code, code));
  if (existing) return existing;
  const [created] = await db.insert(groups).values({ name, code, notes }).returning();
  return created;
}

async function ensureCrew(groupId: string, number: number, name: string, color: string, reserve = false) {
  const [existing] = await db.select().from(crews).where(and(eq(crews.groupId, groupId), eq(crews.number, number)));
  if (existing) return existing;
  const [created] = await db.insert(crews).values({ groupId, number, name, color, reserve }).returning();
  return created;
}

const [migrated] = await db.select().from(groups).where(eq(groups.code, "MIGRATED"));
const zap = migrated ?? await ensureGroup("Запоріжжя", "zap", "Operational group");
if (migrated) {
  await db.update(groups)
    .set({ name: "Запоріжжя", code: "zap", notes: "Operational group", updatedAt: new Date() })
    .where(eq(groups.id, migrated.id));
}

let [defaultType] = await db.select().from(batteryTypes).where(eq(batteryTypes.name, "LiPo 12S 54Ah"));
if (!defaultType) {
  [defaultType] = await db.insert(batteryTypes).values({
    name: "LiPo 12S 54Ah",
    capacityAh: "54",
    minVoltage: "36",
    maxVoltage: "50.4",
    cellCount: 12,
    chemistry: "LiPo"
  }).returning();
}

const zapCrews = [
  await ensureCrew(zap.id, 1, "Червона", "#E05252"),
  await ensureCrew(zap.id, 2, "Зелена", "#54C878"),
  await ensureCrew(zap.id, 3, "Жовта", "#E7C34F")
];

const crewCodes = ["RED", "GREEN", "YELLOW"];

for (const [crewIndex, crew] of zapCrews.entries()) {
  const assigned = await db.select().from(batteries).where(eq(batteries.crewId, crew.id));

  if (!assigned.length) {
    const created = await db.insert(batteries).values(
      Array.from({ length: 7 }, (_, packIndex) => ({
        crewId: crew.id,
        typeId: defaultType.id,
        serialNumber: `ZAP-${crewCodes[crewIndex]}-${String(packIndex + 1).padStart(2, "0")}`,
        label: `${crew.name} ${String(packIndex + 1).padStart(2, "0")}`,
        state: "ready" as const
      }))
    ).returning();

    await db.insert(transfers).values(
      created.map(battery => ({
        batteryId: battery.id,
        toCrewId: battery.crewId,
        notes: "Initial assignment"
      }))
    );
  }
}

const demoUsers = [
  { username: process.env.SEED_ADMIN_USERNAME ?? "admin", password: process.env.SEED_ADMIN_PASSWORD ?? "SarmatAdmin!2026", role: "SUPER_ADMIN" as const, groupId: null, crewId: null },
  { username: "zap-admin", password: "ZapAdmin!2026", role: "GROUP_ADMIN" as const, groupId: zap.id, crewId: null },
  { username: "red", password: "RedCrew!2026", role: "CREW" as const, groupId: zap.id, crewId: zapCrews[0].id },
  { username: "green", password: "GreenCrew!2026", role: "CREW" as const, groupId: zap.id, crewId: zapCrews[1].id },
  { username: "yellow", password: "YellowCrew!2026", role: "CREW" as const, groupId: zap.id, crewId: zapCrews[2].id }
];

for (const demo of demoUsers) {
  const [present] = await db.select().from(users).where(eq(users.username, demo.username));
  if (!present) await db.insert(users).values({ username: demo.username, passwordHash: await hash(demo.password, 12), role: demo.role, groupId: demo.groupId, crewId: demo.crewId });
}

console.log("Zaporizhzhia group, crews, batteries, and credentials seeded.");
await connection.end();
