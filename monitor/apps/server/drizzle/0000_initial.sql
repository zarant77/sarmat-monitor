CREATE TYPE "battery_state" AS ENUM ('ready', 'charging', 'in_use', 'storage', 'service', 'retired');
CREATE TYPE "health_state" AS ENUM ('good', 'warning', 'danger');
CREATE TYPE "cycle_event_type" AS ENUM ('cycle', 'charge', 'discharge', 'maintenance', 'repair', 'inspection', 'service', 'retirement', 'note');
CREATE TYPE "user_role" AS ENUM ('SUPER_ADMIN', 'GROUP_ADMIN', 'CREW');
CREATE TYPE "checker_module" AS ENUM ('A', 'B');

CREATE TABLE "groups" (
  "id" uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  "name" varchar(120) NOT NULL,
  "code" varchar(40) NOT NULL DEFAULT '',
  "notes" text NOT NULL DEFAULT '',
  "enabled" boolean NOT NULL DEFAULT true,
  "created_at" timestamptz NOT NULL DEFAULT now(),
  "updated_at" timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE "crews" (
  "id" uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  "group_id" uuid NOT NULL REFERENCES "groups"("id") ON DELETE RESTRICT,
  "number" integer NOT NULL,
  "name" varchar(100) NOT NULL,
  "color" varchar(7) NOT NULL DEFAULT '#B7EF55',
  "notes" text NOT NULL DEFAULT '',
  "enabled" boolean NOT NULL DEFAULT true,
  "reserve" boolean NOT NULL DEFAULT false,
  "created_at" timestamptz NOT NULL DEFAULT now(),
  "updated_at" timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT "crews_group_number_unique" UNIQUE ("group_id", "number"),
  CONSTRAINT "crews_number_positive_check" CHECK ("number" > 0),
  CONSTRAINT "crews_color_hex_check" CHECK ("color" ~ '^#[0-9A-Fa-f]{6}$')
);

CREATE TABLE "users" (
  "id" uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  "username" varchar(80) NOT NULL UNIQUE,
  "password_hash" text NOT NULL,
  "role" "user_role" NOT NULL,
  "group_id" uuid REFERENCES "groups"("id") ON DELETE RESTRICT,
  "crew_id" uuid REFERENCES "crews"("id") ON DELETE RESTRICT,
  "enabled" boolean NOT NULL DEFAULT true,
  "created_at" timestamptz NOT NULL DEFAULT now(),
  "updated_at" timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT "user_role_scope_check" CHECK (
    ("role" = 'SUPER_ADMIN' AND "group_id" IS NULL AND "crew_id" IS NULL) OR
    ("role" = 'GROUP_ADMIN' AND "group_id" IS NOT NULL AND "crew_id" IS NULL) OR
    ("role" = 'CREW' AND "group_id" IS NOT NULL AND "crew_id" IS NOT NULL)
  )
);

CREATE TABLE "sessions" (
  "id" uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  "user_id" uuid NOT NULL REFERENCES "users"("id") ON DELETE CASCADE,
  "token_hash" varchar(64) NOT NULL,
  "expires_at" timestamptz NOT NULL,
  "created_at" timestamptz NOT NULL DEFAULT now(),
  "last_seen_at" timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE "battery_types" (
  "id" uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  "name" varchar(100) NOT NULL UNIQUE,
  "capacity_ah" numeric(8,2) NOT NULL,
  "min_voltage" numeric(8,3) NOT NULL,
  "max_voltage" numeric(8,3) NOT NULL,
  "cell_count" integer NOT NULL,
  "chemistry" varchar(50) NOT NULL,
  "created_at" timestamptz NOT NULL DEFAULT now(),
  "updated_at" timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT "battery_types_capacity_positive" CHECK ("capacity_ah" > 0),
  CONSTRAINT "battery_types_voltage_range" CHECK ("min_voltage" > 0 AND "max_voltage" > "min_voltage"),
  CONSTRAINT "battery_types_cells_positive" CHECK ("cell_count" > 0)
);

CREATE TABLE "batteries" (
  "id" uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  "crew_id" uuid NOT NULL REFERENCES "crews"("id"),
  "type_id" uuid NOT NULL REFERENCES "battery_types"("id") ON DELETE RESTRICT,
  "serial_number" varchar(100) NOT NULL UNIQUE,
  "label" varchar(100) NOT NULL,
  "state" "battery_state" NOT NULL DEFAULT 'ready',
  "archived_at" timestamptz,
  "notes" text NOT NULL DEFAULT '',
  "created_at" timestamptz NOT NULL DEFAULT now(),
  "updated_at" timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE "checker_photo_sets" (
  "id" uuid PRIMARY KEY,
  "battery_id" uuid NOT NULL REFERENCES "batteries"("id") ON DELETE CASCADE,
  "created_by_user_id" uuid NOT NULL REFERENCES "users"("id") ON DELETE RESTRICT,
  "created_at" timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE "checker_images" (
  "id" uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  "photo_set_id" uuid NOT NULL REFERENCES "checker_photo_sets"("id") ON DELETE CASCADE,
  "battery_id" uuid NOT NULL REFERENCES "batteries"("id") ON DELETE CASCADE,
  "module" "checker_module" NOT NULL,
  "mime_type" varchar(50) NOT NULL,
  "byte_size" integer NOT NULL,
  "width" integer NOT NULL,
  "height" integer NOT NULL,
  "image_data" bytea NOT NULL,
  "uploaded_by_user_id" uuid NOT NULL REFERENCES "users"("id") ON DELETE RESTRICT,
  "uploaded_at" timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE "measurements" (
  "id" uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  "battery_id" uuid NOT NULL REFERENCES "batteries"("id") ON DELETE CASCADE,
  "photo_set_id" uuid REFERENCES "checker_photo_sets"("id") ON DELETE SET NULL,
  "total_voltage" numeric(8,3) NOT NULL,
  "cell_voltages" jsonb NOT NULL,
  "min_cell_voltage" numeric(6,3) NOT NULL,
  "max_cell_voltage" numeric(6,3) NOT NULL,
  "cell_delta" numeric(6,3) NOT NULL,
  "charge_percent" integer,
  "temperature_c" numeric(5,2),
  "health" "health_state" NOT NULL,
  "warning_threshold_v" numeric(6,3) NOT NULL,
  "danger_threshold_v" numeric(6,3) NOT NULL,
  "notes" text NOT NULL DEFAULT '',
  "corrected_at" timestamptz,
  "corrected_by_user_id" uuid REFERENCES "users"("id"),
  "measured_at" timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE "cycle_events" (
  "id" uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  "battery_id" uuid NOT NULL REFERENCES "batteries"("id") ON DELETE CASCADE,
  "type" "cycle_event_type" NOT NULL,
  "cycle_delta" integer NOT NULL DEFAULT 0,
  "flight_minutes" integer,
  "notes" text NOT NULL DEFAULT '',
  "inferred" boolean NOT NULL DEFAULT false,
  "source_measurement_id" uuid REFERENCES "measurements"("id") ON DELETE CASCADE,
  "occurred_at" timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE "transfers" (
  "id" uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  "battery_id" uuid NOT NULL REFERENCES "batteries"("id") ON DELETE CASCADE,
  "from_crew_id" uuid REFERENCES "crews"("id"),
  "to_crew_id" uuid NOT NULL REFERENCES "crews"("id"),
  "notes" text NOT NULL DEFAULT '',
  "transferred_at" timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE "settings" (
  "id" integer PRIMARY KEY DEFAULT 1,
  "warning_cell_delta_v" numeric(6,3) NOT NULL DEFAULT 0.100,
  "danger_cell_delta_v" numeric(6,3) NOT NULL DEFAULT 0.200,
  "charged_threshold_percent" integer NOT NULL DEFAULT 90,
  "discharged_threshold_percent" integer NOT NULL DEFAULT 20,
  "updated_at" timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX "groups_code_idx" ON "groups" ("code");
CREATE INDEX "crews_group_idx" ON "crews" ("group_id");
CREATE INDEX "users_group_idx" ON "users" ("group_id");
CREATE INDEX "users_crew_idx" ON "users" ("crew_id");
CREATE UNIQUE INDEX "sessions_token_hash_idx" ON "sessions" ("token_hash");
CREATE INDEX "sessions_user_idx" ON "sessions" ("user_id");
CREATE INDEX "batteries_type_idx" ON "batteries" ("type_id");
CREATE INDEX "checker_photo_sets_battery_idx" ON "checker_photo_sets" ("battery_id");
CREATE UNIQUE INDEX "checker_images_set_module_idx" ON "checker_images" ("photo_set_id", "module");
CREATE INDEX "checker_images_battery_idx" ON "checker_images" ("battery_id");
CREATE INDEX "measurements_battery_date_idx" ON "measurements" ("battery_id", "measured_at" DESC);
CREATE INDEX "measurements_photo_set_idx" ON "measurements" ("photo_set_id");
CREATE INDEX "cycles_battery_date_idx" ON "cycle_events" ("battery_id", "occurred_at" DESC);
CREATE UNIQUE INDEX "cycle_events_source_measurement_idx" ON "cycle_events" ("source_measurement_id");
CREATE INDEX "transfers_battery_date_idx" ON "transfers" ("battery_id", "transferred_at" DESC);

INSERT INTO "settings" ("id") VALUES (1);
