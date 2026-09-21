ALTER TABLE "batteries" ADD COLUMN "active_since" timestamp with time zone;--> statement-breakpoint
CREATE UNIQUE INDEX "batteries_one_active_per_crew" ON "batteries" USING btree ("crew_id") WHERE "batteries"."active_since" is not null;--> statement-breakpoint
ALTER TABLE "settings" ADD COLUMN "charge_event_deadband_percent" integer DEFAULT 2 NOT NULL;
