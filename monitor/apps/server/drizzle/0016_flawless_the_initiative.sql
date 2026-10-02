ALTER TABLE "batteries" ADD COLUMN "actual_capacity_ah" numeric(8, 2);--> statement-breakpoint
ALTER TABLE "batteries" ADD COLUMN "internal_resistance_milliohms_override" numeric(10, 3);--> statement-breakpoint
ALTER TABLE "battery_types" ADD COLUMN "internal_resistance_milliohms" numeric(10, 3);--> statement-breakpoint
ALTER TABLE "battery_voltage_events" ADD COLUMN "consumed_mah" numeric(14, 3);--> statement-breakpoint
ALTER TABLE "battery_voltage_events" ADD COLUMN "consumption_complete" boolean DEFAULT false NOT NULL;