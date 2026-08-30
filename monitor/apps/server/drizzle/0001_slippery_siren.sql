ALTER TABLE "crews" ADD COLUMN "secret" varchar(200) DEFAULT '' NOT NULL;
--> statement-breakpoint
CREATE UNIQUE INDEX "crews_secret_unique" ON "crews" USING btree ("secret") WHERE "crews"."secret" <> '';
