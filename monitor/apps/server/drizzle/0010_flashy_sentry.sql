CREATE TABLE "motor_installations" (
	"id" uuid PRIMARY KEY DEFAULT gen_random_uuid() NOT NULL,
	"motor_id" uuid NOT NULL,
	"drone_id" uuid NOT NULL,
	"position_number" integer NOT NULL,
	"installed_at" timestamp with time zone DEFAULT now() NOT NULL,
	"removed_at" timestamp with time zone,
	"installed_by_user_id" uuid,
	"removed_by_user_id" uuid,
	"install_notes" text DEFAULT '' NOT NULL,
	"removal_notes" text DEFAULT '' NOT NULL,
	CONSTRAINT "motor_installations_position_check" CHECK ("motor_installations"."position_number" > 0)
);
--> statement-breakpoint
ALTER TABLE "motor_installations" ADD CONSTRAINT "motor_installations_motor_id_motors_id_fk" FOREIGN KEY ("motor_id") REFERENCES "public"."motors"("id") ON DELETE restrict ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "motor_installations" ADD CONSTRAINT "motor_installations_drone_id_drones_id_fk" FOREIGN KEY ("drone_id") REFERENCES "public"."drones"("id") ON DELETE restrict ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "motor_installations" ADD CONSTRAINT "motor_installations_installed_by_user_id_users_id_fk" FOREIGN KEY ("installed_by_user_id") REFERENCES "public"."users"("id") ON DELETE set null ON UPDATE no action;--> statement-breakpoint
ALTER TABLE "motor_installations" ADD CONSTRAINT "motor_installations_removed_by_user_id_users_id_fk" FOREIGN KEY ("removed_by_user_id") REFERENCES "public"."users"("id") ON DELETE set null ON UPDATE no action;--> statement-breakpoint
CREATE INDEX "motor_installations_drone_history_idx" ON "motor_installations" USING btree ("drone_id","installed_at");--> statement-breakpoint
CREATE INDEX "motor_installations_motor_history_idx" ON "motor_installations" USING btree ("motor_id","installed_at");--> statement-breakpoint
CREATE UNIQUE INDEX "motor_installations_active_motor_unique" ON "motor_installations" USING btree ("motor_id") WHERE "motor_installations"."removed_at" is null;--> statement-breakpoint
CREATE UNIQUE INDEX "motor_installations_active_position_unique" ON "motor_installations" USING btree ("drone_id","position_number") WHERE "motor_installations"."removed_at" is null;