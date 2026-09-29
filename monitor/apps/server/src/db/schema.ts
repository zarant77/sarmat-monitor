import { sql } from "drizzle-orm";
import { boolean, check, index, integer, jsonb, numeric, pgEnum, pgTable, text, timestamp, uniqueIndex, uuid, varchar } from "drizzle-orm/pg-core";

export const batteryStateEnum = pgEnum("battery_state", ["ready", "charging", "in_use", "storage", "service", "retired"]);
export const healthStateEnum = pgEnum("health_state", ["good", "warning", "danger"]);
export const cycleEventTypeEnum = pgEnum("cycle_event_type", ["charge", "discharge", "archive", "restore", "retirement"]);
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
  activeSince: timestamp("active_since", { withTimezone: true }),
  archivedAt: timestamp("archived_at", { withTimezone: true }),
  notes: text("notes").default("").notNull(),
  ...timestamps
}, table => [
  index("batteries_type_idx").on(table.typeId),
  uniqueIndex("batteries_one_active_per_crew").on(table.crewId).where(sql`${table.activeSince} is not null`)
]);

export const drones = pgTable("drones", {
  id: uuid("id").defaultRandom().primaryKey(),
  crewId: uuid("crew_id").references(() => crews.id, { onDelete: "restrict" }).notNull(),
  name: varchar("name", { length: 100 }).notNull(),
  model: varchar("model", { length: 120 }).notNull(),
  motorCount: integer("motor_count").notNull(),
  initialFlightSeconds: integer("initial_flight_seconds").default(0).notNull(),
  notes: text("notes").default("").notNull(),
  retiredAt: timestamp("retired_at", { withTimezone: true }),
  ...timestamps
}, table => [
  index("drones_crew_idx").on(table.crewId),
  check("drones_motor_count_check", sql`${table.motorCount} in (4, 6)`),
  check("drones_initial_flight_seconds_check", sql`${table.initialFlightSeconds} >= 0`)
]);

export const motors = pgTable("motors", {
  id: uuid("id").defaultRandom().primaryKey(),
  groupId: uuid("group_id").references(() => groups.id, { onDelete: "restrict" }).notNull(),
  serialNumber: varchar("serial_number", { length: 100 }).notNull().unique(),
  type: varchar("type", { length: 3 }).$type<"CV" | "CCV">().default("CV").notNull(),
  initialFlightSeconds: integer("initial_flight_seconds").default(0).notNull(),
  notes: text("notes").default("").notNull(),
  retiredAt: timestamp("retired_at", { withTimezone: true }),
  ...timestamps
}, table => [
  index("motors_group_idx").on(table.groupId),
  check("motors_type_check", sql`${table.type} in ('CV', 'CCV')`),
  check("motors_initial_flight_seconds_check", sql`${table.initialFlightSeconds} >= 0`)
]);

export const motorInstallations = pgTable("motor_installations", {
  id: uuid("id").defaultRandom().primaryKey(),
  motorId: uuid("motor_id").references(() => motors.id, { onDelete: "restrict" }).notNull(),
  droneId: uuid("drone_id").references(() => drones.id, { onDelete: "restrict" }).notNull(),
  positionNumber: integer("position_number").notNull(),
  installedAt: timestamp("installed_at", { withTimezone: true }).defaultNow().notNull(),
  removedAt: timestamp("removed_at", { withTimezone: true }),
  installedByUserId: uuid("installed_by_user_id").references(() => users.id, { onDelete: "set null" }),
  removedByUserId: uuid("removed_by_user_id").references(() => users.id, { onDelete: "set null" }),
  installNotes: text("install_notes").default("").notNull(),
  removalNotes: text("removal_notes").default("").notNull()
}, table => [
  check("motor_installations_position_check", sql`${table.positionNumber} > 0`),
  index("motor_installations_drone_history_idx").on(table.droneId, table.installedAt),
  index("motor_installations_motor_history_idx").on(table.motorId, table.installedAt),
  uniqueIndex("motor_installations_active_motor_unique").on(table.motorId).where(sql`${table.removedAt} is null`),
  uniqueIndex("motor_installations_active_position_unique").on(table.droneId, table.positionNumber).where(sql`${table.removedAt} is null`)
]);

export const measurements = pgTable("measurements", {
  id: uuid("id").defaultRandom().primaryKey(),
  batteryId: uuid("battery_id").references(() => batteries.id, { onDelete: "cascade" }).notNull(),
  totalVoltage: numeric("total_voltage", { precision: 8, scale: 3 }).notNull(),
  cellVoltages: jsonb("cell_voltages").$type<number[]>().notNull(),
  minCellVoltage: numeric("min_cell_voltage", { precision: 6, scale: 3 }).notNull(),
  maxCellVoltage: numeric("max_cell_voltage", { precision: 6, scale: 3 }).notNull(),
  cellDelta: numeric("cell_delta", { precision: 6, scale: 3 }).notNull(),
  temperatureC: numeric("temperature_c", { precision: 5, scale: 2 }),
  health: healthStateEnum("health").notNull(),
  warningThresholdV: numeric("warning_threshold_v", { precision: 6, scale: 3 }).notNull(),
  dangerThresholdV: numeric("danger_threshold_v", { precision: 6, scale: 3 }).notNull(),
  notes: text("notes").default("").notNull(),
  correctedAt: timestamp("corrected_at", { withTimezone: true }),
  correctedByUserId: uuid("corrected_by_user_id").references(() => users.id),
  measuredAt: timestamp("measured_at", { withTimezone: true }).defaultNow().notNull()
});

// Immutable binding established by the plugin's battery selection dialog.
export const batteryTelemetrySessions = pgTable("battery_telemetry_sessions", {
  id: uuid("id").primaryKey(),
  batteryId: uuid("battery_id").references(() => batteries.id, { onDelete: "cascade" }).notNull(),
  crewId: uuid("crew_id").references(() => crews.id, { onDelete: "restrict" }).notNull(),
  droneId: uuid("drone_id").references(() => drones.id, { onDelete: "restrict" }),
  createdAt: timestamp("created_at", { withTimezone: true }).defaultNow().notNull()
});

export const batteryVoltageEvents = pgTable("battery_voltage_events", {
  id: uuid("id").primaryKey(),
  sessionId: uuid("session_id").references(() => batteryTelemetrySessions.id, { onDelete: "cascade" }).notNull(),
  batteryId: uuid("battery_id").references(() => batteries.id, { onDelete: "cascade" }).notNull(),
  type: varchar("type", { length: 32 }).$type<"vehicle_connected" | "vehicle_disarmed">().notNull(),
  totalVoltage: numeric("total_voltage", { precision: 8, scale: 3 }).notNull(),
  occurredAt: timestamp("occurred_at", { withTimezone: true }).notNull(),
  measuredAt: timestamp("measured_at", { withTimezone: true }).notNull(),
  receivedAt: timestamp("received_at", { withTimezone: true }).defaultNow().notNull()
}, table => [index("battery_voltage_events_history_idx").on(table.batteryId, table.measuredAt)]);

export const droneFlights = pgTable("drone_flights", {
  id: uuid("id").primaryKey(),
  telemetrySessionId: uuid("telemetry_session_id").references(() => batteryTelemetrySessions.id, { onDelete: "restrict" }).notNull(),
  droneId: uuid("drone_id").references(() => drones.id, { onDelete: "restrict" }).notNull(),
  armedAt: timestamp("armed_at", { withTimezone: true }).notNull(),
  disarmedAt: timestamp("disarmed_at", { withTimezone: true }),
  durationSeconds: integer("duration_seconds"),
  excludedAt: timestamp("excluded_at", { withTimezone: true }),
  excludedByUserId: uuid("excluded_by_user_id").references(() => users.id, { onDelete: "set null" }),
  createdAt: timestamp("created_at", { withTimezone: true }).defaultNow().notNull()
}, table => [
  index("drone_flights_drone_history_idx").on(table.droneId, table.armedAt),
  index("drone_flights_session_idx").on(table.telemetrySessionId),
  check("drone_flights_duration_check", sql`${table.durationSeconds} is null or ${table.durationSeconds} >= 0`)
]);

export const flightCorrections = pgTable("flight_corrections", {
  id: uuid("id").defaultRandom().primaryKey(),
  flightId: uuid("flight_id").references(() => droneFlights.id, { onDelete: "cascade" }).notNull(),
  previousArmedAt: timestamp("previous_armed_at", { withTimezone: true }).notNull(),
  previousDisarmedAt: timestamp("previous_disarmed_at", { withTimezone: true }),
  previousExcludedAt: timestamp("previous_excluded_at", { withTimezone: true }),
  newArmedAt: timestamp("new_armed_at", { withTimezone: true }).notNull(),
  newDisarmedAt: timestamp("new_disarmed_at", { withTimezone: true }),
  newExcludedAt: timestamp("new_excluded_at", { withTimezone: true }),
  notes: text("notes").notNull(),
  correctedByUserId: uuid("corrected_by_user_id").references(() => users.id, { onDelete: "set null" }),
  correctedAt: timestamp("corrected_at", { withTimezone: true }).defaultNow().notNull()
}, table => [index("flight_corrections_flight_idx").on(table.flightId, table.correctedAt)]);

export const flightMotors = pgTable("flight_motors", {
  id: uuid("id").defaultRandom().primaryKey(),
  flightId: uuid("flight_id").references(() => droneFlights.id, { onDelete: "cascade" }).notNull(),
  motorId: uuid("motor_id").references(() => motors.id, { onDelete: "restrict" }).notNull(),
  installationId: uuid("installation_id").references(() => motorInstallations.id, { onDelete: "restrict" }).notNull(),
  positionNumber: integer("position_number").notNull()
}, table => [
  uniqueIndex("flight_motors_flight_motor_unique").on(table.flightId, table.motorId),
  uniqueIndex("flight_motors_flight_position_unique").on(table.flightId, table.positionNumber),
  index("flight_motors_motor_idx").on(table.motorId)
]);

export const droneFlightEvents = pgTable("drone_flight_events", {
  id: uuid("id").primaryKey(),
  flightId: uuid("flight_id").references(() => droneFlights.id, { onDelete: "cascade" }).notNull(),
  telemetrySessionId: uuid("telemetry_session_id").references(() => batteryTelemetrySessions.id, { onDelete: "restrict" }).notNull(),
  type: varchar("type", { length: 16 }).$type<"armed" | "disarmed">().notNull(),
  occurredAt: timestamp("occurred_at", { withTimezone: true }).notNull(),
  receivedAt: timestamp("received_at", { withTimezone: true }).defaultNow().notNull()
}, table => [index("drone_flight_events_flight_idx").on(table.flightId)]);

// The receipt and mutation commit together, making offline retries safe.
export const syncOperations = pgTable("sync_operations", {
  id: uuid("id").primaryKey(),
  userId: uuid("user_id").references(() => users.id, { onDelete: "cascade" }).notNull(),
  payloadHash: text("payload_hash").notNull(),
  result: jsonb("result").$type<Record<string, unknown>>().notNull(),
  receivedAt: timestamp("received_at", { withTimezone: true }).defaultNow().notNull()
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
  criticalChargePercent: integer("critical_charge_percent").default(20).notNull(),
  chargeEventDeadbandPercent: integer("charge_event_deadband_percent").default(2).notNull(),
  updatedAt: timestamp("updated_at", { withTimezone: true }).defaultNow().notNull()
});
