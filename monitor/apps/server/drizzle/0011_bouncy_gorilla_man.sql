CREATE TABLE "drone_flight_events" (
	"id" uuid PRIMARY KEY NOT NULL,
	"flight_id" uuid NOT NULL,
	"telemetry_session_id" uuid NOT NULL,
	"type" varchar(16) NOT NULL,
	"occurred_at" timestamp with time zone NOT NULL,
	"received_at" timestamp with time zone DEFAULT now() NOT NULL
);
--> statement-breakpoint
CREATE TABLE "drone_flights" (
	"id" uuid PRIMARY KEY NOT NULL,
	"telemetry_session_id" uuid NOT NULL,
	"drone_id" uuid NOT NULL,
	"armed_at" timestamp with time zone NOT NULL,
	"disarmed_at" timestamp with time zone,
	"duration_seconds" integer,
	"created_at" timestamp with time zone DEFAULT now() NOT NULL,
	CONSTRAINT "drone_flights_duration_check" CHECK ("drone_flights"."duration_seconds" is null or "drone_flights"."duration_seconds" >= 0)
);
--> statement-breakpoint
CREATE TABLE "flight_motors" (
	"id" uuid PRIMARY KEY DEFAULT gen_random_uuid() NOT NULL,
	"flight_id" uuid NOT NULL,
	"motor_id" uuid NOT NULL,
	"installation_id" uuid NOT NULL,
	"position_number" integer NOT NULL
);
--> statement-breakpoint
ALTER TABLE "battery_telemetry_sessions" ADD COLUMN "drone_id" uuid;--> statement-breakpoint
ALTER TABLE "drone_flight_events" ADD CONSTRAINT "drone_flight_events_flight_id_drone_flights_id_fk" FOREIGN KEY ("flight_id") REFERENCES "public"."drone_flights"("id") ON DELETE cascade ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "drone_flight_events" ADD CONSTRAINT "drone_flight_events_telemetry_session_id_battery_telemetry_sessions_id_fk" FOREIGN KEY ("telemetry_session_id") REFERENCES "public"."battery_telemetry_sessions"("id") ON DELETE restrict ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "drone_flights" ADD CONSTRAINT "drone_flights_telemetry_session_id_battery_telemetry_sessions_id_fk" FOREIGN KEY ("telemetry_session_id") REFERENCES "public"."battery_telemetry_sessions"("id") ON DELETE restrict ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "drone_flights" ADD CONSTRAINT "drone_flights_drone_id_drones_id_fk" FOREIGN KEY ("drone_id") REFERENCES "public"."drones"("id") ON DELETE restrict ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "flight_motors" ADD CONSTRAINT "flight_motors_flight_id_drone_flights_id_fk" FOREIGN KEY ("flight_id") REFERENCES "public"."drone_flights"("id") ON DELETE cascade ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "flight_motors" ADD CONSTRAINT "flight_motors_motor_id_motors_id_fk" FOREIGN KEY ("motor_id") REFERENCES "public"."motors"("id") ON DELETE restrict ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "flight_motors" ADD CONSTRAINT "flight_motors_installation_id_motor_installations_id_fk" FOREIGN KEY ("installation_id") REFERENCES "public"."motor_installations"("id") ON DELETE restrict ON UPDATE no action;--> statement-breakpoint
CREATE INDEX "drone_flight_events_flight_idx" ON "drone_flight_events" USING btree ("flight_id");--> statement-breakpoint
CREATE INDEX "drone_flights_drone_history_idx" ON "drone_flights" USING btree ("drone_id","armed_at");--> statement-breakpoint
CREATE INDEX "drone_flights_session_idx" ON "drone_flights" USING btree ("telemetry_session_id");--> statement-breakpoint
CREATE UNIQUE INDEX "flight_motors_flight_motor_unique" ON "flight_motors" USING btree ("flight_id","motor_id");--> statement-breakpoint
CREATE UNIQUE INDEX "flight_motors_flight_position_unique" ON "flight_motors" USING btree ("flight_id","position_number");--> statement-breakpoint
CREATE INDEX "flight_motors_motor_idx" ON "flight_motors" USING btree ("motor_id");--> statement-breakpoint
ALTER TABLE "battery_telemetry_sessions" ADD CONSTRAINT "battery_telemetry_sessions_drone_id_drones_id_fk" FOREIGN KEY ("drone_id") REFERENCES "public"."drones"("id") ON DELETE restrict ON UPDATE no action;