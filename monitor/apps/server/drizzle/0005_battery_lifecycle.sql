-- Refuse unexpected legacy records rather than silently discarding history.
DO $$ BEGIN
  IF EXISTS (SELECT 1 FROM "cycle_events" WHERE "type"::text NOT IN ('charge', 'discharge', 'retirement')) THEN
    RAISE EXCEPTION 'Legacy battery events exist. Review them before applying the battery lifecycle migration.';
  END IF;
END $$;--> statement-breakpoint
ALTER TABLE "cycle_events" ALTER COLUMN "type" SET DATA TYPE text;--> statement-breakpoint
DROP TYPE "public"."cycle_event_type";--> statement-breakpoint
CREATE TYPE "public"."cycle_event_type" AS ENUM('charge', 'discharge', 'archive', 'restore', 'retirement');--> statement-breakpoint
ALTER TABLE "cycle_events" ALTER COLUMN "type" SET DATA TYPE "public"."cycle_event_type" USING "type"::"public"."cycle_event_type";--> statement-breakpoint
-- The previous archive endpoint used retired for temporary archiving.
UPDATE "batteries" SET "state" = 'storage', "active_since" = NULL
WHERE "archived_at" IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM "cycle_events" WHERE "battery_id" = "batteries"."id" AND "type" = 'retirement');--> statement-breakpoint
-- Preserve any explicitly recorded retirement as an actual lifecycle state.
UPDATE "batteries" SET "state" = 'retired', "active_since" = NULL,
  "archived_at" = COALESCE("archived_at", "updated_at")
WHERE "state" = 'retired'
   OR EXISTS (SELECT 1 FROM "cycle_events" WHERE "battery_id" = "batteries"."id" AND "type" = 'retirement');--> statement-breakpoint
INSERT INTO "cycle_events" ("battery_id", "type", "occurred_at")
SELECT "id", 'archive', "archived_at" FROM "batteries"
WHERE "archived_at" IS NOT NULL AND "state" <> 'retired';
