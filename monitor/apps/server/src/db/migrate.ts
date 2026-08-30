import { migrate } from "drizzle-orm/postgres-js/migrator";
import { connection, db } from "./index.js";

await migrate(db, { migrationsFolder: new URL("../../drizzle", import.meta.url).pathname });
await connection.end();
console.log("Database migrations complete.");
