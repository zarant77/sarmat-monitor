ALTER TABLE "settings" ALTER COLUMN "discharged_threshold_percent" SET DEFAULT 50;--> statement-breakpoint
UPDATE "settings" SET "discharged_threshold_percent" = 50, "updated_at" = now()
WHERE "id" = 1 AND "discharged_threshold_percent" IN (20, 35);--> statement-breakpoint
DELETE FROM "cycle_events" WHERE "inferred" = true;--> statement-breakpoint
WITH "stable_history" AS (
  SELECT
    "measurements"."id",
    "measurements"."battery_id",
    "measurements"."measured_at",
    CASE
      WHEN "measurements"."charge_percent" >= "settings"."charged_threshold_percent" THEN 'charged'
      WHEN "measurements"."charge_percent" <= "settings"."discharged_threshold_percent" THEN 'discharged'
    END AS "charge_state"
  FROM "measurements"
  CROSS JOIN "settings"
  WHERE "settings"."id" = 1
    AND (
      "measurements"."charge_percent" >= "settings"."charged_threshold_percent"
      OR "measurements"."charge_percent" <= "settings"."discharged_threshold_percent"
    )
), "transitions" AS (
  SELECT *, lag("charge_state") OVER (
    PARTITION BY "battery_id" ORDER BY "measured_at", "id"
  ) AS "previous_state"
  FROM "stable_history"
)
INSERT INTO "cycle_events" (
  "battery_id", "type", "cycle_delta", "notes", "inferred", "source_measurement_id", "occurred_at"
)
SELECT
  "battery_id",
  CASE WHEN "charge_state" = 'charged' THEN 'charge'::"cycle_event_type" ELSE 'discharge'::"cycle_event_type" END,
  CASE WHEN "charge_state" = 'charged' THEN 1 ELSE 0 END,
  '', true, "id", "measured_at"
FROM "transitions"
WHERE ("previous_state" = 'charged' AND "charge_state" = 'discharged')
   OR ("previous_state" = 'discharged' AND "charge_state" = 'charged');
