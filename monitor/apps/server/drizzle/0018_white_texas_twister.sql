ALTER TABLE "batteries" ADD COLUMN "last_active_since" timestamp with time zone;
--> statement-breakpoint
UPDATE "batteries" SET "last_active_since" = "active_since" WHERE "active_since" IS NOT NULL;
