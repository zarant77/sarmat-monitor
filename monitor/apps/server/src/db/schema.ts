import { sql } from "drizzle-orm";
import { boolean, index, integer, jsonb, numeric, pgEnum, pgTable, text, timestamp, uniqueIndex, uuid, varchar } from "drizzle-orm/pg-core";

export const batteryStateEnum = pgEnum("battery_state", ["ready", "charging", "in_use", "storage", "service", "retired"]);
export const healthStateEnum = pgEnum("health_state", ["good", "warning", "danger"]);
export const cycleEventTypeEnum = pgEnum("cycle_event_type", ["cycle", "charge", "discharge", "maintenance", "repair", "inspection", "service", "retirement", "note"]);
export const userRoleEnum = pgEnum("user_role", ["SUPER_ADMIN", "GROUP_ADMIN", "CREW"]);

const timestamps = {
  createdAt: timestamp("created_at", { withTimezone: true }).defaultNow().notNull(),
  updatedAt: timestamp("updated_at", { withTimezone: true }).defaultNow().notNull()
};

export const groups = pgTable("groups", {
  id: uuid("id").defaultRandom().primaryKey(),
  name: varchar("name", { length: 120 }).notNull(),
  code: varchar("code", { length: 40 }).default("").notNull(),
  notes: text("notes").default("").notNull(),
  enabled: boolean("enabled").default(true).notNull(),
  ...timestamps
}, table => [index("groups_code_idx").on(table.code)]);

export const crews = pgTable("crews", {
  id: uuid("id").defaultRandom().primaryKey(),
  groupId: uuid("group_id").references(() => groups.id, { onDelete: "restrict" }).notNull(),
  number: integer("number").notNull(),
  name: varchar("name", { length: 100 }).notNull(),
  color: varchar("color", { length: 7 }).default("#B7EF55").notNull(),
  secret: varchar("secret", { length: 200 }).default("").notNull(),
  notes: text("notes").default("").notNull(),
  enabled: boolean("enabled").default(true).notNull(),
  reserve: boolean("reserve").default(false).notNull(),
  ...timestamps
}, table => [
  uniqueIndex("crews_group_number_unique").on(table.groupId, table.number),
  uniqueIndex("crews_secret_unique").on(table.secret).where(sql`${table.secret} <> ''`),
  index("crews_group_idx").on(table.groupId)
]);

export const users = pgTable("users", {
  id: uuid("id").defaultRandom().primaryKey(),
  username: varchar("username", { length: 80 }).notNull().unique(),
  passwordHash: text("password_hash").notNull(),
  role: userRoleEnum("role").notNull(),
  groupId: uuid("group_id").references(() => groups.id, { onDelete: "restrict" }),
  crewId: uuid("crew_id").references(() => crews.id, { onDelete: "restrict" }),
  enabled: boolean("enabled").default(true).notNull(),
  ...timestamps
}, table => [index("users_group_idx").on(table.groupId), index("users_crew_idx").on(table.crewId)]);

export const sessions = pgTable("sessions", {
  id: uuid("id").defaultRandom().primaryKey(),
  userId: uuid("user_id").references(() => users.id, { onDelete: "cascade" }).notNull(),
  tokenHash: varchar("token_hash", { length: 64 }).notNull(),
  expiresAt: timestamp("expires_at", { withTimezone: true }).notNull(),
  createdAt: timestamp("created_at", { withTimezone: true }).defaultNow().notNull(),
  lastSeenAt: timestamp("last_seen_at", { withTimezone: true }).defaultNow().notNull()
}, table => [uniqueIndex("sessions_token_hash_idx").on(table.tokenHash), index("sessions_user_idx").on(table.userId)]);

export const batteryTypes = pgTable("battery_types", {
  id: uuid("id").defaultRandom().primaryKey(),
  name: varchar("name", { length: 100 }).notNull().unique(),
  capacityAh: numeric("capacity_ah", { precision: 8, scale: 2 }).notNull(),
  minVoltage: numeric("min_voltage", { precision: 8, scale: 3 }).notNull(),
  maxVoltage: numeric("max_voltage", { precision: 8, scale: 3 }).notNull(),
  cellCount: integer("cell_count").notNull(),
  chemistry: varchar("chemistry", { length: 50 }).notNull(),
  ...timestamps
});

export const batteries = pgTable("batteries", {
  id: uuid("id").defaultRandom().primaryKey(),
  crewId: uuid("crew_id").references(() => crews.id).notNull(),
  typeId: uuid("type_id").references(() => batteryTypes.id, { onDelete: "restrict" }).notNull(),
  serialNumber: varchar("serial_number", { length: 100 }).notNull().unique(),
  label: varchar("label", { length: 100 }).notNull(),
  state: batteryStateEnum("state").default("ready").notNull(),
  archivedAt: timestamp("archived_at", { withTimezone: true }),
  notes: text("notes").default("").notNull(),
  ...timestamps
}, table => [index("batteries_type_idx").on(table.typeId)]);

export const measurements = pgTable("measurements", {
  id: uuid("id").defaultRandom().primaryKey(),
  batteryId: uuid("battery_id").references(() => batteries.id, { onDelete: "cascade" }).notNull(),
  totalVoltage: numeric("total_voltage", { precision: 8, scale: 3 }).notNull(),
  cellVoltages: jsonb("cell_voltages").$type<number[]>().notNull(),
  minCellVoltage: numeric("min_cell_voltage", { precision: 6, scale: 3 }).notNull(),
  maxCellVoltage: numeric("max_cell_voltage", { precision: 6, scale: 3 }).notNull(),
  cellDelta: numeric("cell_delta", { precision: 6, scale: 3 }).notNull(),
  chargePercent: integer("charge_percent"),
  temperatureC: numeric("temperature_c", { precision: 5, scale: 2 }),
  health: healthStateEnum("health").notNull(),
  warningThresholdV: numeric("warning_threshold_v", { precision: 6, scale: 3 }).notNull(),
  dangerThresholdV: numeric("danger_threshold_v", { precision: 6, scale: 3 }).notNull(),
  notes: text("notes").default("").notNull(),
  correctedAt: timestamp("corrected_at", { withTimezone: true }),
  correctedByUserId: uuid("corrected_by_user_id").references(() => users.id),
  measuredAt: timestamp("measured_at", { withTimezone: true }).defaultNow().notNull()
});

export const cycleEvents = pgTable("cycle_events", {
  id: uuid("id").defaultRandom().primaryKey(),
  batteryId: uuid("battery_id").references(() => batteries.id, { onDelete: "cascade" }).notNull(),
  type: cycleEventTypeEnum("type").notNull(),
  cycleDelta: integer("cycle_delta").default(0).notNull(),
  flightMinutes: integer("flight_minutes"),
  notes: text("notes").default("").notNull(),
  inferred: boolean("inferred").default(false).notNull(),
  sourceMeasurementId: uuid("source_measurement_id").references(() => measurements.id, { onDelete: "cascade" }),
  occurredAt: timestamp("occurred_at", { withTimezone: true }).defaultNow().notNull()
}, table => [uniqueIndex("cycle_events_source_measurement_idx").on(table.sourceMeasurementId)]);

export const transfers = pgTable("transfers", {
  id: uuid("id").defaultRandom().primaryKey(),
  batteryId: uuid("battery_id").references(() => batteries.id, { onDelete: "cascade" }).notNull(),
  fromCrewId: uuid("from_crew_id").references(() => crews.id),
  toCrewId: uuid("to_crew_id").references(() => crews.id).notNull(),
  notes: text("notes").default("").notNull(),
  transferredAt: timestamp("transferred_at", { withTimezone: true }).defaultNow().notNull()
});

export const settings = pgTable("settings", {
  id: integer("id").primaryKey().default(1),
  warningCellDeltaV: numeric("warning_cell_delta_v", { precision: 6, scale: 3 }).default("0.100").notNull(),
  dangerCellDeltaV: numeric("danger_cell_delta_v", { precision: 6, scale: 3 }).default("0.200").notNull(),
  chargedThresholdPercent: integer("charged_threshold_percent").default(90).notNull(),
  dischargedThresholdPercent: integer("discharged_threshold_percent").default(50).notNull(),
  updatedAt: timestamp("updated_at", { withTimezone: true }).defaultNow().notNull()
});
