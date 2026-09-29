CREATE TABLE "flight_corrections" (
	"id" uuid PRIMARY KEY DEFAULT gen_random_uuid() NOT NULL,
	"flight_id" uuid NOT NULL,
	"previous_armed_at" timestamp with time zone NOT NULL,
	"previous_disarmed_at" timestamp with time zone,
	"previous_excluded_at" timestamp with time zone,
	"new_armed_at" timestamp with time zone NOT NULL,
	"new_disarmed_at" timestamp with time zone,
	"new_excluded_at" timestamp with time zone,
	"notes" text NOT NULL,
	"corrected_by_user_id" uuid,
	"corrected_at" timestamp with time zone DEFAULT now() NOT NULL
);
--> statement-breakpoint
ALTER TABLE "drone_flights" ADD COLUMN "excluded_at" timestamp with time zone;--> statement-breakpoint
ALTER TABLE "drone_flights" ADD COLUMN "excluded_by_user_id" uuid;--> statement-breakpoint
ALTER TABLE "flight_corrections" ADD CONSTRAINT "flight_corrections_flight_id_drone_flights_id_fk" FOREIGN KEY ("flight_id") REFERENCES "public"."drone_flights"("id") ON DELETE cascade ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "flight_corrections" ADD CONSTRAINT "flight_corrections_corrected_by_user_id_users_id_fk" FOREIGN KEY ("corrected_by_user_id") REFERENCES "public"."users"("id") ON DELETE set null ON UPDATE no action;--> statement-breakpoint
CREATE INDEX "flight_corrections_flight_idx" ON "flight_corrections" USING btree ("flight_id","corrected_at");--> statement-breakpoint
ALTER TABLE "drone_flights" ADD CONSTRAINT "drone_flights_excluded_by_user_id_users_id_fk" FOREIGN KEY ("excluded_by_user_id") REFERENCES "public"."users"("id") ON DELETE set null ON UPDATE no action;