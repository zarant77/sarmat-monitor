import { z } from "zod";

export const batteryStates = ["ready", "charging", "in_use", "storage", "service", "retired"] as const;
export const healthStates = ["good", "warning", "danger"] as const;
export const cycleEventTypes = ["charge", "discharge", "archive", "restore", "retirement"] as const;
export const checkerModules = ["A", "B"] as const;
export const userRoles = ["SUPER_ADMIN", "GROUP_ADMIN", "CREW"] as const;
export const motorStatuses = ["stock", "installed", "retired"] as const;
export const motorTypes = ["CV", "CCV"] as const;

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

const equipmentHoursSchema = z.coerce.number().int().min(0).max(2147483647).default(0);

export const droneInputSchema = z.object({
  crewId: z.uuid(),
  name: z.string().trim().min(1).max(100),
  model: z.string().trim().min(1).max(120),
  motorCount: z.coerce.number().int().refine(value => value === 4 || value === 6, "Motor count must be 4 or 6"),
  initialFlightSeconds: equipmentHoursSchema,
  notes: z.string().trim().max(2000).optional().default("")
});

export const droneUpdateSchema = droneInputSchema.partial().refine(
  value => Object.keys(value).length > 0,
  "At least one change is required"
);

export const motorInputSchema = z.object({
  groupId: z.uuid().optional(),
  serialNumber: z.string().trim().min(1).max(100),
  type: z.enum(motorTypes),
  initialFlightSeconds: equipmentHoursSchema,
  notes: z.string().trim().max(2000).optional().default("")
});

export const motorUpdateSchema = motorInputSchema.omit({ groupId: true }).partial().refine(
  value => Object.keys(value).length > 0,
  "At least one change is required"
);

export const motorAssignmentSchema = z.object({
  motorId: z.uuid(),
  notes: z.string().trim().max(1000).optional().default("")
});

export const motorRemovalSchema = z.object({
  notes: z.string().trim().max(1000).optional().default("")
});

export const flightCorrectionSchema = z.object({
  armedAt: z.iso.datetime({ offset: true }).optional(),
  disarmedAt: z.iso.datetime({ offset: true }).nullable().optional(),
  excluded: z.boolean().optional(),
  notes: z.string().trim().min(1).max(1000)
}).refine(value => value.armedAt !== undefined || value.disarmedAt !== undefined || value.excluded !== undefined, "At least one flight change is required");

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
  dischargedThresholdPercent: z.coerce.number().int().min(0).max(70),
  criticalChargePercent: z.coerce.number().int().min(0).max(69),
  chargeEventDeadbandPercent: z.coerce.number().int().min(1).max(20)
}).superRefine((value, context) => {
  if (value.dangerCellDeltaV <= value.warningCellDeltaV) context.addIssue({ code: "custom", message: "Danger threshold must be greater than warning threshold", path: ["dangerCellDeltaV"] });
  if (value.chargedThresholdPercent <= value.dischargedThresholdPercent) context.addIssue({ code: "custom", message: "Charged threshold must be greater than discharged threshold", path: ["chargedThresholdPercent"] });
  if (value.criticalChargePercent >= value.dischargedThresholdPercent) context.addIssue({ code: "custom", message: "Critical charge threshold must be lower than discharged threshold", path: ["criticalChargePercent"] });
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
export type DroneInput = z.infer<typeof droneInputSchema>;
export type DroneUpdate = z.infer<typeof droneUpdateSchema>;
export type MotorInput = z.infer<typeof motorInputSchema>;
export type MotorUpdate = z.infer<typeof motorUpdateSchema>;
export type MotorAssignment = z.infer<typeof motorAssignmentSchema>;
export type MotorRemoval = z.infer<typeof motorRemovalSchema>;
export type FlightCorrectionInput = z.infer<typeof flightCorrectionSchema>;
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
export type MotorStatus = typeof motorStatuses[number];
export type MotorType = typeof motorTypes[number];

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
export interface Drone extends Omit<DroneInput, "initialFlightSeconds"> {
  id: string; groupId: string; groupName: string; crewNumber: number; crewName: string;
  initialFlightSeconds: number; totalFlightSeconds: number; status: "active" | "retired";
  installedMotorCount: number; openFlightStartedAt: string | null; retiredAt: string | null; createdAt: string; updatedAt: string;
}
export interface Motor extends Omit<MotorInput, "groupId" | "initialFlightSeconds"> {
  id: string; groupId: string; groupName: string; initialFlightSeconds: number; totalFlightSeconds: number;
  currentDroneId: string | null; currentDroneName: string | null; positionNumber: number | null;
  status: MotorStatus; retiredAt: string | null; createdAt: string; updatedAt: string;
}
export interface MotorInstallation {
  id: string; motorId: string; serialNumber: string; type: MotorType; droneId: string; droneName: string; positionNumber: number;
  installedAt: string; removedAt: string | null; installedByUsername: string | null; removedByUsername: string | null;
  installNotes: string; removalNotes: string; active: boolean;
}
export interface FlightMotorSnapshot { motorId: string; serialNumber: string; type: MotorType; positionNumber: number }
export interface FlightCorrection {
  id: string; previousArmedAt: string; previousDisarmedAt: string | null; newArmedAt: string;
  newDisarmedAt: string | null; previousExcluded: boolean; newExcluded: boolean;
  notes: string; correctedByUsername: string | null; correctedAt: string;
}
export interface DroneFlight {
  id: string; droneId: string; armedAt: string; disarmedAt: string | null; durationSeconds: number | null;
  excludedAt: string | null; status: "armed" | "completed" | "excluded"; motors: FlightMotorSnapshot[]; corrections: FlightCorrection[];
}
export interface DroneMotorSlot { positionNumber: number; installation: MotorInstallation | null }
export interface DroneDetail extends Drone { slots: DroneMotorSlot[]; installationHistory: MotorInstallation[]; flights: DroneFlight[] }
export interface MotorDetail extends Motor { installationHistory: MotorInstallation[] }
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
export interface BatteryVoltageEvent {
  id: string; batteryId: string; sessionId: string; type: "vehicle_connected" | "vehicle_disarmed";
  totalVoltage: number; source: "mission_planner"; occurredAt: string; measuredAt: string; receivedAt: string;
}
export interface Battery {
  id: string; crewId: string; groupId: string; groupName: string; crewNumber: number; crewName: string; crewColor: string; typeId: string; typeName: string; serialNumber: string;
  label: string; capacityAh: number; minVoltage: number; maxVoltage: number; cellCount: number; chemistry: string; state: BatteryState;
  notes: string; cycleCount: number; latestMeasurement: Measurement | null;
  latestVoltageEvent?: BatteryVoltageEvent | null;
  activeSince: string | null;
  archivedAt?: string | null; createdAt: string; updatedAt: string;
}
export interface BatteryDetail extends Battery {
  voltageEvents?: BatteryVoltageEvent[];
  measurements: Measurement[]; cycleEvents: CycleEvent[]; transfers: TransferEvent[];
}
export interface Thresholds { warningCellDeltaV: number; dangerCellDeltaV: number; chargedThresholdPercent: number; dischargedThresholdPercent: number; criticalChargePercent: number; chargeEventDeadbandPercent: number }
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
