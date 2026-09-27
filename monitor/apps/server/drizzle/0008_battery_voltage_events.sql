CREATE TABLE "battery_telemetry_sessions" (
	"id" uuid PRIMARY KEY NOT NULL,
	"battery_id" uuid NOT NULL,
	"crew_id" uuid NOT NULL,
	"created_at" timestamp with time zone DEFAULT now() NOT NULL
);
--> statement-breakpoint
CREATE TABLE "battery_voltage_events" (
	"id" uuid PRIMARY KEY NOT NULL,
	"session_id" uuid NOT NULL,
	"battery_id" uuid NOT NULL,
	"type" varchar(32) NOT NULL,
	"total_voltage" numeric(8, 3) NOT NULL,
	"occurred_at" timestamp with time zone NOT NULL,
	"measured_at" timestamp with time zone NOT NULL,
	"received_at" timestamp with time zone DEFAULT now() NOT NULL
);
--> statement-breakpoint
ALTER TABLE "battery_telemetry_sessions" ADD CONSTRAINT "battery_telemetry_sessions_battery_id_batteries_id_fk" FOREIGN KEY ("battery_id") REFERENCES "public"."batteries"("id") ON DELETE cascade ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "battery_telemetry_sessions" ADD CONSTRAINT "battery_telemetry_sessions_crew_id_crews_id_fk" FOREIGN KEY ("crew_id") REFERENCES "public"."crews"("id") ON DELETE restrict ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "battery_voltage_events" ADD CONSTRAINT "battery_voltage_events_session_id_battery_telemetry_sessions_id_fk" FOREIGN KEY ("session_id") REFERENCES "public"."battery_telemetry_sessions"("id") ON DELETE cascade ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "battery_voltage_events" ADD CONSTRAINT "battery_voltage_events_battery_id_batteries_id_fk" FOREIGN KEY ("battery_id") REFERENCES "public"."batteries"("id") ON DELETE cascade ON UPDATE no action;--> statement-breakpoint
CREATE INDEX "battery_voltage_events_history_idx" ON "battery_voltage_events" USING btree ("battery_id","measured_at");