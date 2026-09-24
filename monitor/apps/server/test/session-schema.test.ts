import { describe, expect, it } from "vitest";
import { getTableConfig } from "drizzle-orm/pg-core";
import { sessions } from "../src/db/schema.js";

describe("multi-device session storage", () => {
  it("allows many sessions per user while keeping every token unique", () => {
    const indexes = getTableConfig(sessions).indexes.map(index => ({
      unique: index.config.unique,
      columns: index.config.columns.map(column => "name" in column ? column.name : "")
    }));
    expect(indexes.some(index => index.unique && index.columns.includes("user_id"))).toBe(false);
    expect(indexes.some(index => index.unique && index.columns.includes("token_hash"))).toBe(true);
  });
});
