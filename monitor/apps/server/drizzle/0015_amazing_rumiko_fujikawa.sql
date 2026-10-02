ALTER TABLE "battery_voltage_events" ADD COLUMN "current_amps" numeric(10, 3);--> statement-breakpoint
ALTER TABLE "measurements" ADD COLUMN "current_amps" numeric(10, 3) DEFAULT '0' NOT NULL;