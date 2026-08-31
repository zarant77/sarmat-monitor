import type { AuthUser, Battery, BatteryDetail, BatteryInput, BatteryType, BatteryTypeInput, BatteryTypeUpdate, BatteryUpdate, Crew, CrewInput, CrewUpdate, CredentialInput, CredentialUpdate, CycleEventInput, Group, GroupAdminCredentialInput, GroupInput, GroupUpdate, ManagedUser, MeasurementInput, MeasurementPreview, MeasurementPreviewInput, TelemetryResponse, ThresholdInput, Thresholds } from "@sbm/shared";

const BASE = import.meta.env.VITE_API_URL ?? "";
export class ApiError extends Error {
  constructor(message: string, readonly code?: string, readonly details?: unknown) { super(message); }
}
async function request<T>(path: string, options?: RequestInit): Promise<T> {
  const response = await fetch(`${BASE}${path}`, { ...options, credentials: "include", headers: { "Content-Type": "application/json", ...options?.headers } });
  if (!response.ok) {
    const body = await response.json().catch(() => ({ error: response.statusText }));
    throw new ApiError(body.error ?? response.statusText, body.code, body.issues ?? body.partial);
  }
  if (response.status === 204) return undefined as T;
  return response.json();
}
const json = (method: string, body: unknown): RequestInit => ({ method, body: JSON.stringify(body) });

export const api = {
  login: (username: string, password: string) => request<AuthUser>("/api/auth/login", json("POST", { username, password })),
  me: () => request<AuthUser>("/api/auth/me"),
  logout: () => request<{ ok: true }>("/api/auth/logout", json("POST", {})),
  groups: () => request<Group[]>("/api/groups"),
  group: (id: string) => request<Group>(`/api/groups/${id}`),
  createGroup: (data: GroupInput) => request<Group>("/api/groups", json("POST", data)),
  updateGroup: (id: string, data: GroupUpdate) => request<Group>(`/api/groups/${id}`, json("PATCH", data)),
  deleteGroup: (id: string) => request<void>(`/api/groups/${id}`, { method: "DELETE" }),
  crews: (groupId?: string) => request<Crew[]>(`/api/crews${groupId ? `?groupId=${groupId}` : ""}`),
  telemetry: (groupId?: string) => request<TelemetryResponse>(`/api/telemetry${groupId ? `?groupId=${groupId}` : ""}`),
  createCrew: (data: CrewInput) => request<Crew>("/api/crews", json("POST", data)),
  updateCrew: (id: string, data: CrewUpdate) => request<Crew>(`/api/crews/${id}`, json("PATCH", data)),
  deleteCrew: (id: string) => request<void>(`/api/crews/${id}`, { method: "DELETE" }),
  batteryTypes: () => request<BatteryType[]>("/api/battery-types"),
  createBatteryType: (data: BatteryTypeInput) => request<BatteryType>("/api/battery-types", json("POST", data)),
  updateBatteryType: (id: string, data: BatteryTypeUpdate) => request<BatteryType>(`/api/battery-types/${id}`, json("PATCH", data)),
  deleteBatteryType: (id: string) => request<void>(`/api/battery-types/${id}`, { method: "DELETE" }),
  users: (crewId?: string, groupId?: string) => request<ManagedUser[]>(`/api/admin/users?${new URLSearchParams({ ...(crewId ? { crewId } : {}), ...(groupId ? { groupId } : {}) })}`),
  createGroupAdmin: (data: GroupAdminCredentialInput) => request<ManagedUser>("/api/admin/group-users", json("POST", data)),
  createCredential: (crewId: string, data: CredentialInput) => request<ManagedUser>(`/api/admin/crews/${crewId}/users`, json("POST", data)),
  updateCredential: (id: string, data: CredentialUpdate) => request<ManagedUser>(`/api/admin/users/${id}`, json("PATCH", data)),
  deleteCredential: (id: string) => request<void>(`/api/admin/users/${id}`, { method: "DELETE" }),
  batteries: (crewId?: string, includeArchived = false, groupId?: string) => request<Battery[]>(`/api/batteries?${new URLSearchParams({ ...(crewId ? { crewId } : {}), ...(groupId ? { groupId } : {}), ...(includeArchived ? { includeArchived: "true" } : {}) })}`),
  battery: (id: string) => request<BatteryDetail>(`/api/batteries/${id}`),
  createBattery: (data: BatteryInput) => request<Battery>("/api/batteries", json("POST", data)),
  updateBattery: (id: string, data: BatteryUpdate) => request<Battery>(`/api/batteries/${id}`, json("PATCH", data)),
  transfer: (id: string, crewId: string, notes = "") => request(`/api/batteries/${id}/transfer`, json("POST", { crewId, notes })),
  measurement: (id: string, data: MeasurementInput) => request(`/api/batteries/${id}/measurements`, json("POST", data)),
  measurementPreview: (id: string, data: MeasurementPreviewInput) => request<MeasurementPreview>(`/api/batteries/${id}/measurement-preview`, json("POST", data)),
  cycle: (id: string, data: CycleEventInput) => request(`/api/batteries/${id}/cycles`, json("POST", data)),
  correctMeasurement: (id: string, data: Partial<MeasurementInput>) => request(`/api/admin/measurements/${id}`, json("PATCH", data)),
  archiveBattery: (id: string) => request(`/api/admin/batteries/${id}/archive`, json("POST", {})),
  restoreBattery: (id: string) => request(`/api/admin/batteries/${id}/restore`, json("POST", {})),
  thresholds: () => request<Thresholds>("/api/settings/thresholds"),
  updateThresholds: (data: ThresholdInput) => request<Thresholds>("/api/settings/thresholds", json("PUT", data))
};
