import { createHash, randomBytes } from "node:crypto";
import type { FastifyReply, FastifyRequest } from "fastify";
import { eq } from "drizzle-orm";
import { db } from "./db/index.js";
import { crews, groups, sessions, users } from "./db/schema.js";

export const SESSION_COOKIE = "sbm_session";
const SESSION_DAYS = 7;
export type UserRole = "SUPER_ADMIN" | "GROUP_ADMIN" | "CREW";

export interface Actor {
  userId: string;
  username: string;
  role: UserRole;
  groupId: string | null;
  groupName: string | null;
  crewId: string | null;
  crewNumber: number | null;
  crewName: string | null;
  crewColor: string | null;
  userEnabled: boolean;
  groupEnabled: boolean;
  crewEnabled: boolean;
}

declare module "fastify" { interface FastifyRequest { actor: Actor | null } }

export const hashSessionToken = (token: string) => createHash("sha256").update(token).digest("hex");

export function isAccountEnabled(role: UserRole, userEnabled: boolean, groupEnabled = true, crewEnabled = true): boolean {
  return userEnabled && (role === "SUPER_ADMIN" || (groupEnabled && (role === "GROUP_ADMIN" || crewEnabled)));
}

export function assertActiveActor(actor: Actor | null): asserts actor is Actor {
  if (!actor) throw Object.assign(new Error("Authentication required"), { statusCode: 401 });
  if (!isAccountEnabled(actor.role, actor.userEnabled, actor.groupEnabled, actor.crewEnabled)) {
    throw Object.assign(new Error("Account is disabled"), { statusCode: 403 });
  }
}

export function assertSuperAdmin(actor: Actor | null): asserts actor is Actor {
  assertActiveActor(actor);
  if (actor.role !== "SUPER_ADMIN") throw Object.assign(new Error("Super administrator access required"), { statusCode: 403 });
}

export function assertGroupAdministrator(actor: Actor | null): asserts actor is Actor {
  assertActiveActor(actor);
  if (actor.role === "CREW") throw Object.assign(new Error("Administrator access required"), { statusCode: 403 });
}

export function assertGroupAccess(actor: Actor, groupId: string): void {
  assertActiveActor(actor);
  if (actor.role !== "SUPER_ADMIN" && actor.groupId !== groupId) {
    throw Object.assign(new Error("Resource not found"), { statusCode: 404 });
  }
}

export function assertCrewAccess(actor: Actor, crewId: string, groupId?: string): void {
  assertActiveActor(actor);
  if (actor.role === "CREW" && actor.crewId !== crewId) {
    throw Object.assign(new Error("Resource not found"), { statusCode: 404 });
  }
  if (actor.role === "GROUP_ADMIN" && groupId && actor.groupId !== groupId) {
    throw Object.assign(new Error("Resource not found"), { statusCode: 404 });
  }
}

export function assertTransferAccess(actor: Actor, sourceGroupId: string, targetGroupId: string): void {
  assertGroupAdministrator(actor);
  assertGroupAccess(actor, sourceGroupId);
  assertGroupAccess(actor, targetGroupId);
  if (actor.role === "GROUP_ADMIN" && sourceGroupId !== targetGroupId) {
    throw Object.assign(new Error("Cross-group transfer requires super administrator access"), { statusCode: 403 });
  }
}

export function effectiveCrewId(actor: Actor, requestedCrewId?: string): string | undefined {
  assertActiveActor(actor);
  return actor.role === "CREW" ? actor.crewId ?? undefined : requestedCrewId;
}

export async function loadActor(request: FastifyRequest): Promise<Actor | null> {
  const token = request.cookies[SESSION_COOKIE];
  if (!token) return null;
  const [row] = await db.select({ session: sessions, user: users, crew: crews, group: groups })
    .from(sessions)
    .innerJoin(users, eq(sessions.userId, users.id))
    .leftJoin(crews, eq(users.crewId, crews.id))
    .leftJoin(groups, eq(users.groupId, groups.id))
    .where(eq(sessions.tokenHash, hashSessionToken(token)));
  if (!row || row.session.expiresAt <= new Date()) return null;
  return {
    userId: row.user.id, username: row.user.username, role: row.user.role,
    groupId: row.user.groupId, groupName: row.group?.name ?? null,
    crewId: row.user.crewId, crewNumber: row.crew?.number ?? null, crewName: row.crew?.name ?? null, crewColor: row.crew?.color ?? null,
    userEnabled: row.user.enabled, groupEnabled: row.group?.enabled ?? true, crewEnabled: row.crew?.enabled ?? true
  };
}

export async function createSession(userId: string, reply: FastifyReply): Promise<void> {
  const token = randomBytes(32).toString("base64url");
  const expiresAt = new Date(Date.now() + SESSION_DAYS * 24 * 60 * 60 * 1000);
  await db.insert(sessions).values({ userId, tokenHash: hashSessionToken(token), expiresAt });
  reply.setCookie(SESSION_COOKIE, token, { httpOnly: true, sameSite: "strict", secure: process.env.NODE_ENV === "production", path: "/", expires: expiresAt });
}

export async function revokeSession(request: FastifyRequest, reply: FastifyReply): Promise<void> {
  const token = request.cookies[SESSION_COOKIE];
  if (token) await db.delete(sessions).where(eq(sessions.tokenHash, hashSessionToken(token)));
  reply.clearCookie(SESSION_COOKIE, { path: "/" });
}

export function publicActor(actor: Actor) {
  return {
    id: actor.userId, username: actor.username, role: actor.role,
    groupId: actor.groupId, groupName: actor.groupName,
    crewId: actor.crewId, crewNumber: actor.crewNumber, crewName: actor.crewName, crewColor: actor.crewColor
  };
}
