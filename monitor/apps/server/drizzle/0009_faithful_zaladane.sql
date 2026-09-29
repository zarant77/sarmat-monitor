CREATE TABLE "drones" (
	"id" uuid PRIMARY KEY DEFAULT gen_random_uuid() NOT NULL,
	"crew_id" uuid NOT NULL,
	"name" varchar(100) NOT NULL,
	"model" varchar(120) NOT NULL,
	"motor_count" integer NOT NULL,
	"initial_flight_seconds" integer DEFAULT 0 NOT NULL,
	"notes" text DEFAULT '' NOT NULL,
	"retired_at" timestamp with time zone,
	"created_at" timestamp with time zone DEFAULT now() NOT NULL,
	"updated_at" timestamp with time zone DEFAULT now() NOT NULL,
	CONSTRAINT "drones_motor_count_check" CHECK ("drones"."motor_count" in (4, 6)),
	CONSTRAINT "drones_initial_flight_seconds_check" CHECK ("drones"."initial_flight_seconds" >= 0)
);
--> statement-breakpoint
CREATE TABLE "motors" (
	"id" uuid PRIMARY KEY DEFAULT gen_random_uuid() NOT NULL,
	"group_id" uuid NOT NULL,
	"serial_number" varchar(100) NOT NULL,
	"initial_flight_seconds" integer DEFAULT 0 NOT NULL,
	"notes" text DEFAULT '' NOT NULL,
	"retired_at" timestamp with time zone,
	"created_at" timestamp with time zone DEFAULT now() NOT NULL,
	"updated_at" timestamp with time zone DEFAULT now() NOT NULL,
	CONSTRAINT "motors_serial_number_unique" UNIQUE("serial_number"),
	CONSTRAINT "motors_initial_flight_seconds_check" CHECK ("motors"."initial_flight_seconds" >= 0)
);
--> statement-breakpoint
ALTER TABLE "drones" ADD CONSTRAINT "drones_crew_id_crews_id_fk" FOREIGN KEY ("crew_id") REFERENCES "public"."crews"("id") ON DELETE restrict ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "motors" ADD CONSTRAINT "motors_group_id_groups_id_fk" FOREIGN KEY ("group_id") REFERENCES "public"."groups"("id") ON DELETE restrict ON UPDATE no action;--> statement-breakpoint
CREATE INDEX "drones_crew_idx" ON "drones" USING btree ("crew_id");--> statement-breakpoint
CREATE INDEX "motors_group_idx" ON "motors" USING btree ("group_id");