ALTER TABLE "motors" ADD COLUMN "type" varchar(3) DEFAULT 'CV' NOT NULL;--> statement-breakpoint
ALTER TABLE "motors" ADD CONSTRAINT "motors_type_check" CHECK ("motors"."type" in ('CV', 'CCV'));