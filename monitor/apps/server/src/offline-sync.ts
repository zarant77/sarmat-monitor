import { createHash } from "node:crypto";
import { z } from "zod";
import { measurementInputSchema } from "@sbm/shared";

const base = z.object({
  id: z.string().uuid(), batteryId: z.string().uuid(), crewId: z.string().uuid(),
  occurredAt: z.string().datetime()
});
export const syncOperationSchema = z.discriminatedUnion("kind", [
  base.extend({ kind: z.literal("measurement"), ...measurementInputSchema.shape }),
  base.extend({
    kind: z.literal("active"), active: z.boolean(),
    expectedActiveId: z.string().uuid().nullable(), expectedActiveSince: z.string().datetime().nullable()
  })
]);
export const syncPayloadHash = (payload: unknown) => createHash("sha256").update(JSON.stringify(payload)).digest("hex");
