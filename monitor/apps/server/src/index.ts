import "dotenv/config";
import { buildApp } from "./app.js";
import { connection } from "./db/index.js";

const app = await buildApp();
const port = Number(process.env.PORT ?? 3000);

const shutdown = async () => { await app.close(); await connection.end(); process.exit(0); };
process.on("SIGINT", shutdown);
process.on("SIGTERM", shutdown);

await app.listen({ host: "0.0.0.0", port });
