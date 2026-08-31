ALTER TABLE "measurements" DROP CONSTRAINT IF EXISTS "measurements_photo_set_id_checker_photo_sets_id_fk";
--> statement-breakpoint
DROP INDEX IF EXISTS "measurements_photo_set_idx";--> statement-breakpoint
ALTER TABLE "measurements" DROP COLUMN IF EXISTS "photo_set_id";--> statement-breakpoint
DROP TABLE IF EXISTS "checker_images";--> statement-breakpoint
DROP TABLE IF EXISTS "checker_photo_sets";--> statement-breakpoint
DROP TYPE IF EXISTS "public"."checker_module";
