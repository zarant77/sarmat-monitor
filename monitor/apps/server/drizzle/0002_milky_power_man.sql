ALTER TABLE "measurements" DROP CONSTRAINT "measurements_photo_set_id_checker_photo_sets_id_fk";
--> statement-breakpoint
DROP INDEX "measurements_photo_set_idx";--> statement-breakpoint
ALTER TABLE "measurements" DROP COLUMN "photo_set_id";--> statement-breakpoint
DROP TABLE "checker_images";--> statement-breakpoint
DROP TABLE "checker_photo_sets";--> statement-breakpoint
DROP TYPE "public"."checker_module";
