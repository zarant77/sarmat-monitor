export function isCompleteCell(value: string, min: number, max: number) {
  return /^\d+\.\d{2}$/.test(value) && Number(value) >= min && Number(value) <= max;
}

export type CellEntryError = "format" | "overflow" | "range";
export type CellEntryResult = { cells: string[]; focusIndex: number | null; error?: CellEntryError };

/** Scan once around the pack; never overwrite another populated cell. */
export function nextEmptyCell(cells: string[], current: number): number | null {
  for (let offset = 1; offset < cells.length; offset++) {
    const index = (current + offset) % cells.length;
    if (cells[index] === "") return index;
  }
  return null;
}

function parseEntry(text: string): string[] | null {
  if (text === "") return [""];
  const tokens = text.trim().split(/[\s;]+/);
  const values: string[] = [];
  for (const token of tokens) {
    if (/^\d+$/.test(token)) {
      for (let index = 0; index < token.length; index += 3) {
        const digits = token.slice(index, index + 3);
        values.push(digits.length === 3 ? `${digits[0]}.${digits.slice(1)}` : digits);
      }
    } else if (/^\d[.,]\d{0,2}$/.test(token)) {
      values.push(token.replace(",", "."));
    } else return null;
  }
  // Only the last cell can be unfinished.
  if (values.slice(0, -1).some(value => !/^\d\.\d{2}$/.test(value))) return null;
  return values;
}

/** Interpret both a typed value and a pasted stream, without losing excess data silently. */
export function enterCells(current: string[], start: number, text: string, min: number, max: number): CellEntryResult {
  const values = parseEntry(text);
  if (!values) return { cells: current, focusIndex: null, error: "format" };
  const cells = [...current];
  let index = start;
  for (let position = 0; position < values.length; position++) {
    const value = values[position];
    cells[index] = value;
    const complete = isCompleteCell(value, min, max);
    const outOfRange = /^\d\.\d{2}$/.test(value) && !complete;
    if (outOfRange) {
      // A single invalid entry remains editable; a batch is rejected as a whole.
      return { cells: values.length === 1 ? cells : current, focusIndex: null, error: "range" };
    }
    const next = nextEmptyCell(cells, index);
    if (position === values.length - 1) return { cells, focusIndex: complete ? next : index };
    if (next === null) return { cells: current, focusIndex: null, error: "overflow" };
    index = next;
  }
  return { cells, focusIndex: null };
}
