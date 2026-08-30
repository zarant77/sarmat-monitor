import { describe, expect, it } from "vitest";
import { assertActiveActor, assertCrewAccess, assertGroupAccess, assertTransferAccess, effectiveCrewId, isAccountEnabled, type Actor } from "../src/auth.js";

const base = { crewNumber: null, crewName: null, crewColor: null, userEnabled: true, groupEnabled: true, crewEnabled: true };
const superAdmin: Actor = { ...base, userId: "super", username: "super", role: "SUPER_ADMIN", groupId: null, groupName: null, crewId: null };
const groupAdminA: Actor = { ...base, userId: "ga", username: "group-a-admin", role: "GROUP_ADMIN", groupId: "group-a", groupName: "A", crewId: null };
const crewA: Actor = { ...base, userId: "a", username: "crew-a", role: "CREW", groupId: "group-a", groupName: "A", crewId: "crew-a", crewNumber: 1, crewName: "A1", crewColor: "#B7EF55" };

describe("hierarchical authorization policy", () => {
  it("allows SUPER_ADMIN to access every group and crew", () => {
    expect(() => assertGroupAccess(superAdmin, "group-a")).not.toThrow();
    expect(() => assertGroupAccess(superAdmin, "group-b")).not.toThrow();
    expect(() => assertCrewAccess(superAdmin, "crew-b", "group-b")).not.toThrow();
  });

  it("allows GROUP_ADMIN to access its group and denies another group", () => {
    expect(() => assertGroupAccess(groupAdminA, "group-a")).not.toThrow();
    expect(() => assertGroupAccess(groupAdminA, "group-b")).toThrowError("Resource not found");
    expect(() => assertCrewAccess(groupAdminA, "crew-a", "group-a")).not.toThrow();
    expect(() => assertCrewAccess(groupAdminA, "crew-b", "group-b")).toThrowError("Resource not found");
  });

  it("allows GROUP_ADMIN transfers inside its group but rejects cross-group transfers", () => {
    expect(() => assertTransferAccess(groupAdminA, "group-a", "group-a")).not.toThrow();
    expect(() => assertTransferAccess(groupAdminA, "group-a", "group-b")).toThrow();
  });

  it("allows SUPER_ADMIN to transfer batteries between groups", () => {
    expect(() => assertTransferAccess(superAdmin, "group-a", "group-b")).not.toThrow();
  });

  it("scopes CREW lists to its authenticated crew", () => {
    expect(effectiveCrewId(crewA)).toBe("crew-a");
    expect(effectiveCrewId(crewA, "crew-b")).toBe("crew-a");
  });

  it("allows CREW to access its battery and denies another crew in the same group", () => {
    expect(() => assertCrewAccess(crewA, "crew-a", "group-a")).not.toThrow();
    expect(() => assertCrewAccess(crewA, "crew-a2", "group-a")).toThrowError("Resource not found");
  });

  it("denies CREW access across groups, including altered resource ids", () => {
    expect(() => assertCrewAccess(crewA, "crew-b", "group-b")).toThrowError("Resource not found");
    expect(() => assertGroupAccess(crewA, "group-b")).toThrowError("Resource not found");
  });

  it("rejects disabled users, groups, and crews", () => {
    expect(isAccountEnabled("GROUP_ADMIN", true, false, true)).toBe(false);
    expect(isAccountEnabled("CREW", true, true, false)).toBe(false);
    expect(() => assertActiveActor({ ...groupAdminA, groupEnabled: false })).toThrowError("Account is disabled");
    expect(() => assertActiveActor({ ...crewA, crewEnabled: false })).toThrowError("Account is disabled");
    expect(() => assertActiveActor({ ...crewA, userEnabled: false })).toThrowError("Account is disabled");
  });
});
