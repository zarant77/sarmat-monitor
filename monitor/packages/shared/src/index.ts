import { z } from "zod";

export const batteryStates = ["ready", "charging", "in_use", "storage", "service", "retired"] as const;
export const healthStates = ["good", "warning", "danger"] as const;
export const cycleEventTypes = ["charge", "discharge", "archive", "restore", "retirement"] as const;
export const checkerModules = ["A", "B"] as const;
export const userRoles = ["SUPER_ADMIN", "GROUP_ADMIN", "CREW"] as const;

export const groupInputSchema = z.object({
  name: z.string().trim().min(2).max(120),
  code: z.string().trim().max(40).optional().default(""),
  notes: z.string().trim().max(2000).optional().default(""),
  enabled: z.boolean().optional().default(true)
});

export const groupUpdateSchema = groupInputSchema.partial().refine(
  value => Object.keys(value).length > 0,
  "At least one change is required"
);

export const crewInputSchema = z.object({
  groupId: z.uuid().optional(),
  number: z.coerce.number().int().positive().max(9999),
  name: z.string().trim().min(2).max(100),
  color: z.string().regex(/^#[0-9A-Fa-f]{6}$/).default("#B7EF55"),
  secret: z.string().trim().max(200).optional().default(""),
  notes: z.string().trim().max(1000).optional().default(""),
  enabled: z.boolean().optional().default(true),
  reserve: z.boolean().optional().default(false)
});

export const crewUpdateSchema = z.object({
  number: z.coerce.number().int().positive().max(9999).optional(),
  name: z.string().trim().min(2).max(100).optional(),
  color: z.string().regex(/^#[0-9A-Fa-f]{6}$/).optional(),
  secret: z.string().trim().max(200).optional(),
  notes: z.string().trim().max(1000).optional(),
  enabled: z.boolean().optional(),
  reserve: z.boolean().optional()
}).refine(value => Object.keys(value).length > 0, "At least one change is required");

export const batteryInputSchema = z.object({
  crewId: z.uuid().optional(),
  typeId: z.uuid(),
  serialNumber: z.string().trim().min(1).max(100),
  label: z.string().trim().min(1).max(100),
  state: z.enum(["ready", "charging", "in_use", "storage", "service"]).default("ready"),
  notes: z.string().trim().max(2000).optional().default("")
});

export const batteryUpdateSchema = batteryInputSchema.omit({ crewId: true }).partial();
export const transferInputSchema = z.object({ crewId: z.uuid(), notes: z.string().trim().max(500).optional() });

const batteryTypeFields = {
  name: z.string().trim().min(2).max(100),
  capacityAh: z.coerce.number().positive().max(10000),
  minVoltage: z.coerce.number().positive().max(1000),
  maxVoltage: z.coerce.number().positive().max(1000),
  cellCount: z.coerce.number().int().min(1).max(48),
  chemistry: z.string().trim().min(2).max(50)
};

export const batteryTypeInputSchema = z.object(batteryTypeFields).refine(value => value.maxVoltage > value.minVoltage, {
  message: "Maximum voltage must be greater than minimum voltage",
  path: ["maxVoltage"]
});

export const batteryTypeUpdateSchema = z.object({
  name: batteryTypeFields.name.optional(), capacityAh: batteryTypeFields.capacityAh.optional(),
  minVoltage: batteryTypeFields.minVoltage.optional(), maxVoltage: batteryTypeFields.maxVoltage.optional(),
  cellCount: batteryTypeFields.cellCount.optional(), chemistry: batteryTypeFields.chemistry.optional()
}).refine(
  value => Object.keys(value).length > 0,
  "At least one change is required"
);

export const measurementInputSchema = z.object({
  cellVoltages: z.array(z.coerce.number().positive().max(20)).min(1).max(48),
  notes: z.string().trim().max(1000).optional().default("")
});

export const checkerModuleCellsSchema = z.object({
  cells: z.array(z.coerce.number().positive().max(20)).length(6)
});

export const measurementPreviewInputSchema = z.object({
  A: checkerModuleCellsSchema,
  B: checkerModuleCellsSchema
});

export const lifecycleInputSchema = z.object({
  notes: z.string().trim().max(1000).optional().default("")
});

export const thresholdInputSchema = z.object({
  warningCellDeltaV: z.coerce.number().positive().max(2),
  dangerCellDeltaV: z.coerce.number().positive().max(3),
  chargedThresholdPercent: z.coerce.number().int().min(51).max(100),
  dischargedThresholdPercent: z.coerce.number().int().min(0).max(50),
  chargeEventDeadbandPercent: z.coerce.number().int().min(1).max(20)
}).superRefine((value, context) => {
  if (value.dangerCellDeltaV <= value.warningCellDeltaV) context.addIssue({ code: "custom", message: "Danger threshold must be greater than warning threshold", path: ["dangerCellDeltaV"] });
  if (value.chargedThresholdPercent <= value.dischargedThresholdPercent) context.addIssue({ code: "custom", message: "Charged threshold must be greater than discharged threshold", path: ["chargedThresholdPercent"] });
});

export const loginInputSchema = z.object({
  username: z.string().trim().min(1).max(80),
  password: z.string().min(8).max(200)
});

export const credentialInputSchema = z.object({
  username: z.string().trim().min(3).max(80).regex(/^[a-zA-Z0-9._-]+$/),
  password: z.string().min(10).max(200),
  enabled: z.boolean().optional().default(true)
});

export const credentialUpdateSchema = z.object({
  username: z.string().trim().min(3).max(80).regex(/^[a-zA-Z0-9._-]+$/).optional(),
  password: z.string().min(10).max(200).optional(),
  enabled: z.boolean().optional()
}).refine(value => Object.keys(value).length > 0, "At least one change is required");

export const groupAdminCredentialInputSchema = credentialInputSchema.extend({ groupId: z.uuid() });

export const measurementCorrectionSchema = measurementInputSchema.partial().refine(
  value => Object.keys(value).length > 0,
  "At least one change is required"
);

export type CrewInput = z.infer<typeof crewInputSchema>;
export type GroupInput = z.infer<typeof groupInputSchema>;
export type GroupUpdate = z.infer<typeof groupUpdateSchema>;
export type CrewUpdate = z.infer<typeof crewUpdateSchema>;
export type BatteryInput = z.infer<typeof batteryInputSchema>;
export type BatteryUpdate = z.infer<typeof batteryUpdateSchema>;
export type BatteryTypeInput = z.infer<typeof batteryTypeInputSchema>;
export type BatteryTypeUpdate = z.infer<typeof batteryTypeUpdateSchema>;
export type MeasurementInput = z.infer<typeof measurementInputSchema>;
export type MeasurementPreviewInput = z.infer<typeof measurementPreviewInputSchema>;
export type ThresholdInput = z.infer<typeof thresholdInputSchema>;
export type LoginInput = z.infer<typeof loginInputSchema>;
export type CredentialInput = z.infer<typeof credentialInputSchema>;
export type CredentialUpdate = z.infer<typeof credentialUpdateSchema>;
export type GroupAdminCredentialInput = z.infer<typeof groupAdminCredentialInputSchema>;
export type BatteryState = typeof batteryStates[number];
export type HealthState = typeof healthStates[number];
export type CheckerModule = typeof checkerModules[number];

export interface MeasurementPreview {
  cells: number[];
  moduleATotalVoltage: number;
  moduleBTotalVoltage: number;
  combinedTotalVoltage: number;
  chargePercent: number | null;
  minCellVoltage: number;
  maxCellVoltage: number;
  cellDelta: number;
  health: HealthState;
  warningThresholdV: number;
  dangerThresholdV: number;
}

export interface Group extends GroupInput {
  id: string; crewCount: number; batteryCount: number; warningCount: number; adminCount: number; createdAt: string; updatedAt: string;
}
export interface Crew extends CrewInput { id: string; groupId: string; groupName?: string; batteryCount: number; userCount?: number; createdAt: string; updatedAt: string }
export interface BatteryType extends BatteryTypeInput {
  id: string; batteryCount?: number; createdAt: string; updatedAt: string;
}
export interface Measurement {
  id: string; batteryId: string; totalVoltage: number; cellVoltages: number[];
  minCellVoltage: number; maxCellVoltage: number; cellDelta: number;
  chargePercent: number | null; temperatureC: number | null; health: HealthState;
  warningThresholdV: number; dangerThresholdV: number; notes: string; measuredAt: string;
  correctedAt?: string | null; correctedByUserId?: string | null;
}
export interface CycleEvent {
  id: string; batteryId: string; type: typeof cycleEventTypes[number]; cycleDelta: number;
  flightMinutes: number | null; notes: string; inferred?: boolean; sourceMeasurementId?: string | null; occurredAt: string;
}
export interface TransferEvent {
  id: string; batteryId: string; fromCrewId: string | null; fromCrewName: string | null;
  toCrewId: string; toCrewName: string; notes: string; transferredAt: string;
}
export interface Battery {
  id: string; crewId: string; groupId: string; groupName: string; crewNumber: number; crewName: string; crewColor: string; typeId: string; typeName: string; serialNumber: string;
  label: string; capacityAh: number; minVoltage: number; maxVoltage: number; cellCount: number; chemistry: string; state: BatteryState;
  notes: string; cycleCount: number; latestMeasurement: Measurement | null;
  activeSince: string | null;
  archivedAt?: string | null; createdAt: string; updatedAt: string;
}
export interface BatteryDetail extends Battery {
  measurements: Measurement[]; cycleEvents: CycleEvent[]; transfers: TransferEvent[];
}
export interface Thresholds { warningCellDeltaV: number; dangerCellDeltaV: number; chargedThresholdPercent: number; dischargedThresholdPercent: number; chargeEventDeadbandPercent: number }
export type TelemetrySnapshot = [status: number, ageMs: number, sequence: number, voltage: number | null, current: number | null, satellites: number | null, hdop: number | null, heading: number | null, altitude: number | null, linkRssi: number | null, flags: number];
export interface TelemetryThresholds {
  voltage: { goodMin: number; normalMin: number };
  current: { goodMax: number; normalMax: number };
  satellites: { goodMin: number; normalMin: number };
  hdop: { goodMax: number; normalMax: number };
  linkRssi: { goodMin: number; normalMin: number };
}
export interface CrewTelemetry { id: string; number: number; name: string; color: string; snapshot: TelemetrySnapshot | null; }
export interface TelemetryResponse { thresholds: TelemetryThresholds; crews: CrewTelemetry[]; }
export interface AuthUser {
  id: string; username: string; role: typeof userRoles[number]; groupId: string | null; groupName: string | null; crewId: string | null;
  crewNumber: number | null; crewName: string | null; crewColor: string | null;
}
export interface ManagedUser extends AuthUser { enabled: boolean; createdAt: string; updatedAt: string }
