import "dotenv/config";
import { drizzle } from "drizzle-orm/postgres-js";
import postgres from "postgres";
import * as schema from "./schema.js";

export const connection = postgres(process.env.DATABASE_URL ?? "postgresql://sbm:sbm@localhost:5432/sbm");
export const db = drizzle(connection, { schema });
