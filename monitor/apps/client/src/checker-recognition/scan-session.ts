import type { CheckerModule } from "@sbm/shared";

export type ModuleScans = Partial<Record<CheckerModule, number[]>>;

export const setModuleScan = (state: ModuleScans, module: CheckerModule, cells: number[]): ModuleScans => ({ ...state, [module]: [...cells] });
export const clearModuleScan = (state: ModuleScans, module: CheckerModule): ModuleScans => ({ ...state, [module]: undefined });
export const combinedScans = (state: ModuleScans) => state.A?.length === 6 && state.B?.length === 6 ? [...state.A, ...state.B] : null;
export const correctCombinedCell = (cells: number[], index: number, voltage: number) => cells.map((value, cellIndex) => cellIndex === index ? voltage : value);
