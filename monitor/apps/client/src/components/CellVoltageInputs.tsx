import { useId, useLayoutEffect, useRef, useState } from "react";
import { enterCells, isCompleteCell, type CellEntryError } from "../cell-entry";
import { useI18n } from "../i18n";

export function CellVoltageInputs({ cells, min, max, disabled = false, onChange }: {
  cells: string[]; min: number; max: number; disabled?: boolean; onChange: (cells: string[]) => void;
}) {
  const { t, locale } = useI18n();
  const helpId = useId();
  const inputs = useRef<Array<HTMLInputElement | null>>([]);
  const pendingFocus = useRef<number | null>(null);
  const latestCells = useRef(cells);
  latestCells.current = cells;
  const [error, setError] = useState<CellEntryError | null>(null);
  const [active, setActive] = useState<number | null>(null);
  useLayoutEffect(() => {
    if (pendingFocus.current === null) return;
    const input = inputs.current[pendingFocus.current];
    pendingFocus.current = null;
    if (input && document.activeElement !== input) {
      input.focus();
      // A pasted stream may end mid-cell: continue typing after its remaining digits.
      input.setSelectionRange(input.value.length, input.value.length);
    }
  });
  const update = (index: number, text: string, advance = true) => {
    const result = enterCells(latestCells.current, index, text, min, max);
    latestCells.current = result.cells;
    setError(result.error ?? null);
    pendingFocus.current = advance ? result.focusIndex : null;
    onChange(result.cells);
  };
  const display = (value: string) => locale === "uk" ? value.replace(".", ",") : value;
  return <div className="smart-cell-inputs">
    <p id={helpId} className="manual-input-help">{t("events.smartInputHelp")}</p>
    {Array.from({ length: Math.ceil(cells.length / 6) }, (_, module) => {
      const start = module * 6; const values = cells.slice(start, start + 6);
      return <section className={`checker-module smart-cell-module ${active !== null && Math.floor(active / 6) === module ? "active" : ""}`} key={module}>
        <div className="smart-cell-module-title"><strong>{t("events.module", { module: String.fromCharCode(65 + module) })}</strong><small>{values.filter(value => isCompleteCell(value, min, max)).length}/{values.length}</small></div>
        <div className="checker-cell-row" style={{ gridTemplateColumns: `repeat(${values.length}, minmax(0, 1fr))` }}>
          {values.map((value, offset) => {
            const index = start + offset;
            const invalid = value !== "" && !isCompleteCell(value, min, max);
            return <label key={index}><span>{index + 1}</span><input
              ref={input => { inputs.current[index] = input; }}
              aria-label={`${t("common.cell")} ${index + 1}`} aria-describedby={helpId}
              aria-invalid={invalid || undefined} type="text" inputMode="decimal" autoComplete="off" spellCheck={false}
              placeholder={display("4.21")} value={display(value)} required disabled={disabled}
              onFocus={event => { setActive(index); event.currentTarget.select(); }}
              onBlur={() => { setActive(null); }}
              onChange={event => update(index, event.target.value, !(event.nativeEvent as InputEvent).inputType?.startsWith("delete"))}
              onPaste={event => { event.preventDefault(); update(index, event.clipboardData.getData("text")); }}
              onKeyDown={event => {
                if (event.key === "Backspace" && value === "" && index > 0) {
                  event.preventDefault(); inputs.current[index - 1]?.focus();
                }
                if (event.key === "Enter") {
                  event.preventDefault();
                  // Explicit Enter also accepts a manually entered 4 or 4,2.
                  const normalized = /^\d(?:\.\d{0,2})?$/.test(value) ? Number(value).toFixed(2) : value;
                  update(index, normalized);
                }
              }}/></label>;
          })}
        </div>
      </section>;
    })}
    <p className="smart-cell-range">{t("events.cellRange", { min: display(min.toFixed(2)), max: display(max.toFixed(2)) })}</p>
    {error && <p className="form-error" role="alert">{t(`events.cellEntryErrors.${error}`)}</p>}
  </div>;
}
