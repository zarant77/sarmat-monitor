import { useEffect, useRef, useState, type FormEvent, type TouchEvent } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowLeft, BatteryMedium, Gauge, Pencil, Plus, RotateCcw, SlidersHorizontal, Trash2 } from "lucide-react";
import { Link, useNavigate, useParams } from "react-router-dom";
import type { Measurement, MeasurementPreview } from "@sbm/shared";
import { api } from "../api";
import { useAuth } from "../auth";
import { BatteryForm } from "../components/Forms";
import { HealthBadge } from "../components/HealthBadge";
import { Modal } from "../components/Modal";
import { CellVoltageInputs } from "../components/CellVoltageInputs";
import { isCompleteCell } from "../cell-entry";
import { CrewIdentity } from "../components/CrewIdentity";
import { useI18n } from "../i18n";
import { clientSettings, defaultHistoryFilter, historyEventCategories, type HistoryEventCategory, type HistoryFilterSettings } from "../client-settings";
import { DEFAULT_DISCHARGED_THRESHOLD_PERCENT, batteryCharge, isBatteryDischarged } from "../battery-display";
import { isCompletedBackSwipe } from "../swipe-gesture";

function EventForm({ id, batteryLabel, cellCount, minVoltage, maxVoltage, crewId, archived, admin, onClose }: { batteryLabel: string; crewId: string; archived: boolean; admin: boolean; id: string; cellCount: number; minVoltage: number; maxVoltage: number; onClose: () => void }) {
  const { t } = useI18n(); const qc = useQueryClient();
  const [type, setType] = useState<"check" | "transfer" | "archive" | "retirement">(archived ? "retirement" : "check");
  const crews = useQuery({ queryKey: ["crews"], queryFn: () => api.crews(), enabled: type === "transfer" });
  const mutation = useMutation({ mutationFn: (data: any) => type === "check" ? api.measurement(id, data) : type === "archive" ? api.archiveBattery(id, data.notes) : type === "retirement" ? api.retireBattery(id, data.notes) : api.transfer(id, data.crewId, data.notes), onSuccess: () => { qc.invalidateQueries({ queryKey: ["battery", id] }); qc.invalidateQueries({ queryKey: ["batteries"] }); qc.invalidateQueries({ queryKey: ["crews"] }); onClose(); } });
  const [cells, setCells] = useState(Array.from({ length: cellCount }, () => ""));
  const [combinedPreview, setCombinedPreview] = useState<MeasurementPreview | null>(null);
  const [previewError, setPreviewError] = useState("");
  const numericCells = cells.map(value => Number(value));
  const minCellVoltage = minVoltage / cellCount; const maxCellVoltage = maxVoltage / cellCount + 0.04;
  const cellsComplete = cells.every(value => isCompleteCell(value, minCellVoltage, maxCellVoltage));
  const localTotalVoltage = cellsComplete ? Math.round(numericCells.reduce((sum, voltage) => sum + voltage, 0) * 1000) / 1000 : null;
  const localChargePercent = localTotalVoltage == null ? null : Math.round(Math.max(0, Math.min(100, (localTotalVoltage - minVoltage) / (maxVoltage - minVoltage) * 100)));
  useEffect(() => {
    if (type !== "check" || !cellsComplete || cellCount !== 12) { setCombinedPreview(null); return; }
    let cancelled = false;
    const timer = window.setTimeout(() => {
      void api.measurementPreview(id, { A: { cells: numericCells.slice(0, 6) }, B: { cells: numericCells.slice(6, 12) } })
        .then(result => { if (!cancelled) { setCombinedPreview(result); setPreviewError(""); } })
        .catch(() => { if (!cancelled) { setCombinedPreview(null); setPreviewError(t("recognition.previewError")); } });
    }, 250);
    return () => { cancelled = true; window.clearTimeout(timer); };
  }, [type, cellsComplete, cellCount, id, cells.join("|"), t]);
  const submit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (mutation.isPending || (type === "check" && !cellsComplete)) return;
    const data = new FormData(event.currentTarget);
    if (type === "check") mutation.mutate({ cellVoltages: cells.map(Number), notes: data.get("notes") });
    else mutation.mutate({ crewId: data.get("crewId"), notes: data.get("notes") });
  };
  return <Modal title={batteryLabel} eyebrow={t("battery.history.add")} onClose={onClose}><form onSubmit={submit} className="form-grid">
    {admin && <label className="full">{t("events.eventType")}<select value={type} disabled={mutation.isPending} onChange={event => { setType(event.target.value as typeof type); mutation.reset(); }}>
      <option value="check" disabled={archived}>{t("events.recordCheck")}</option>
      <option value="transfer" disabled={archived}>{t("events.transferBattery")}</option>
      <option value="archive" disabled={archived}>{t("events.archiveBattery")}</option>
      <option value="retirement">{t("events.retireBattery")}</option>
    </select></label>}
    {(type === "archive" || type === "retirement") && <p className="full">{t(type === "archive" ? "events.archiveHelp" : "events.retireHelp")}</p>}
    {type === "check" && <div className="checker-check-layout full">
      <CellVoltageInputs cells={cells} min={minCellVoltage} max={maxCellVoltage} disabled={mutation.isPending} onChange={next => { setCombinedPreview(null); setPreviewError(""); setCells(next); }}/>
      <div className="checker-calculated">
        <span><small>{t("events.totalVoltage")}</small><strong>{combinedPreview?.combinedTotalVoltage.toFixed(2) ?? localTotalVoltage?.toFixed(2) ?? "—"} <em>{t("common.volts")}</em></strong></span>
        <span><small>{t("events.chargePercent")}</small><strong>{combinedPreview?.chargePercent ?? localChargePercent ?? "—"}{(combinedPreview?.chargePercent != null || localChargePercent != null) && <em>%</em>}</strong></span>
      </div>
      {combinedPreview && <div className="recognition-preview"><span><small>A</small><strong>{combinedPreview.moduleATotalVoltage.toFixed(2)} {t("common.volts")}</strong></span><span><small>B</small><strong>{combinedPreview.moduleBTotalVoltage.toFixed(2)} {t("common.volts")}</strong></span><span><small>{t("recognition.minMax")}</small><strong>{combinedPreview.minCellVoltage.toFixed(2)}–{combinedPreview.maxCellVoltage.toFixed(2)}</strong></span><span><small>Δ</small><strong>{combinedPreview.cellDelta.toFixed(2)} {t("common.volts")}</strong></span><HealthBadge health={combinedPreview.health}/></div>}
      {previewError && <p className="form-error">{previewError}</p>}
    </div>}
    {type === "transfer" && <label className="full">{t("events.newCrew")}<select name="crewId" required><option value="">{t("events.selectCrew")}</option>{crews.data?.filter(crew => crew.id !== crewId && crew.enabled).map(crew => <option value={crew.id} key={crew.id}>№{crew.number} · {crew.name}</option>)}</select></label>}
    <label className="full">{t("common.notes")}<textarea name="notes" maxLength={type === "transfer" ? 500 : 1000} disabled={mutation.isPending}/></label>{mutation.error && <p className="form-error">{t("errors.generic")}</p>}<div className="form-actions full"><button type="button" className="button secondary" onClick={onClose}>{t("common.cancel")}</button><button className="button primary" disabled={type === "check" ? (mutation.isPending || !cellsComplete) : mutation.isPending}>{t("events.save")}</button></div>
  </form></Modal>;
}

function CorrectionForm({ measurement, minVoltage, maxVoltage, onClose }: { measurement: Measurement; minVoltage: number; maxVoltage: number; onClose: () => void }) {
  const { t } = useI18n(); const qc = useQueryClient(); const [cells, setCells] = useState(measurement.cellVoltages.map(voltage => voltage.toFixed(2)));
  const mutation = useMutation({ mutationFn: (data: any) => api.correctMeasurement(measurement.id, data), onSuccess: () => { qc.invalidateQueries({ queryKey: ["battery", measurement.batteryId] }); qc.invalidateQueries({ queryKey: ["batteries"] }); onClose(); } });
  const cellsComplete = cells.every(value => isCompleteCell(value, minVoltage / cells.length, maxVoltage / cells.length + 0.04));
  const totalVoltage = cells.reduce((sum, value) => sum + Number(value), 0);
  const chargePercent = Math.round(Math.max(0, Math.min(100, (totalVoltage - minVoltage) / (maxVoltage - minVoltage) * 100)));
  const submit = (event: FormEvent<HTMLFormElement>) => { event.preventDefault(); if (!cellsComplete || mutation.isPending) return; const data = new FormData(event.currentTarget); mutation.mutate({ cellVoltages: cells.map(Number), notes: String(data.get("notes")) }); };
  return <Modal title={t("events.correctMeasurement")} eyebrow={t("events.adminAction")} onClose={onClose}><form className="form-grid" onSubmit={submit}>
    <div className="checker-calculated full"><span><small>{t("events.totalVoltage")}</small><strong>{totalVoltage.toFixed(2)} <em>{t("common.volts")}</em></strong></span><span><small>{t("events.chargePercent")}</small><strong>{chargePercent}<em>%</em></strong></span></div>
    <div className="full"><CellVoltageInputs cells={cells} min={minVoltage / cells.length} max={maxVoltage / cells.length + 0.04} disabled={mutation.isPending} onChange={setCells}/></div>
    <label className="full">{t("events.correctionNote")}<textarea name="notes" defaultValue={measurement.notes}/></label>{mutation.error && <p className="form-error">{t("errors.generic")}</p>}<div className="form-actions full"><button type="button" className="button secondary" onClick={onClose}>{t("common.cancel")}</button><button className="button primary" disabled={!cellsComplete || mutation.isPending}>{t("events.saveCorrection")}</button></div>
  </form></Modal>;
}

export function BatteryDetails() {
  const { id = "" } = useParams(); const auth = useAuth(); const { t, locale } = useI18n(); const admin = auth.user?.role !== "CREW"; const superAdmin = auth.user?.role === "SUPER_ADMIN"; const qc = useQueryClient(); const navigate = useNavigate();
  const query = useQuery({ queryKey: ["battery", id], queryFn: () => api.battery(id) });
  const thresholds = useQuery({ queryKey: ["thresholds"], queryFn: api.thresholds });
  const [form, setForm] = useState<"event" | "edit" | null>(null); const [correction, setCorrection] = useState<Measurement | null>(null); const battery = query.data;
  const [historyFilter, setHistoryFilter] = useState<HistoryFilterSettings>(() => clientSettings.getHistoryFilter());
  const backGesture = useRef({ x: 0, y: 0, ignored: false });
  const updateHistoryFilter = (next: HistoryFilterSettings) => { setHistoryFilter(next); clientSettings.setHistoryFilter(next); };
  const startBackGesture = (event: TouchEvent) => {
    const target = event.target as HTMLElement;
    const touch = event.touches[0];
    backGesture.current = { x: touch.clientX, y: touch.clientY, ignored: Boolean(target.closest("a,button,input,select,textarea,summary,.battery-history-table-wrap,.cell-modules")) };
  };
  const finishBackGesture = (event: TouchEvent) => {
    if (backGesture.current.ignored || form || correction) return;
    const touch = event.changedTouches[0];
    if (isCompletedBackSwipe(touch.clientX - backGesture.current.x, touch.clientY - backGesture.current.y)) navigate(admin ? "/admin/batteries" : "/");
  };
  const archive = useMutation({ mutationFn: () => api.restoreBattery(id), onSuccess: () => { qc.invalidateQueries({ queryKey: ["battery", id] }); qc.invalidateQueries({ queryKey: ["batteries"] }); } });
  const remove = useMutation({ mutationFn: () => api.deleteBattery(id), onSuccess: () => { qc.removeQueries({ queryKey: ["battery", id] }); qc.invalidateQueries({ queryKey: ["batteries"] }); qc.invalidateQueries({ queryKey: ["crews"] }); navigate("/admin/batteries", { replace: true }); } });
  const formatDate = (value: string) => new Intl.DateTimeFormat(locale === "uk" ? "uk-UA" : "en-US", { dateStyle: "medium", timeStyle: "short" }).format(new Date(value));
  if (query.isLoading) return <div className="page"><div className="empty">{t("battery.loading")}</div></div>;
  if (!battery) return <div className="page"><Link to={admin ? "/admin/batteries" : "/"} className="back-link"><ArrowLeft/> {t("battery.back")}</Link><div className="empty error">{t("battery.loadError")}</div></div>;
  const measurement = battery.latestMeasurement; const charge = batteryCharge(battery);
  const discharged = isBatteryDischarged(battery, thresholds.data?.dischargedThresholdPercent ?? DEFAULT_DISCHARGED_THRESHOLD_PERCENT);
  const cellModules = measurement ? ([
    { module: "A", cells: measurement.cellVoltages.slice(0, 6) },
    { module: "B", cells: measurement.cellVoltages.slice(6, 12) }
  ] as const).filter(item => item.cells.length > 0) : [];
  let cumulativeCycles = 0;
  const cycleNumbers = new Map(battery.cycleEvents.slice().sort((a, b) => new Date(a.occurredAt).getTime() - new Date(b.occurredAt).getTime()).map(event => {
    cumulativeCycles += event.cycleDelta;
    return [event.id, cumulativeCycles] as const;
  }));
  const measurementsById = new Map(battery.measurements.map(item => [item.id, item]));
  const historyItems = [
    ...(battery.voltageEvents ?? []).map(item => ({ kind: "voltage" as const, at: item.occurredAt, item })),
    ...battery.measurements.map(item => ({ kind: "measurement" as const, at: item.measuredAt, item })),
    ...battery.cycleEvents.map(item => ({ kind: "event" as const, at: item.occurredAt, item })),
    ...battery.transfers.map(item => ({ kind: "transfer" as const, at: item.transferredAt, item }))
  ].sort((a, b) => {
    const timeDifference = new Date(b.at).getTime() - new Date(a.at).getTime();
    // Newest first: the inferred result follows its measurement chronologically.
    const inferredResult = (entry: typeof a) => entry.kind === "event" && entry.item.inferred && (entry.item.type === "charge" || entry.item.type === "discharge") ? 1 : 0;
    return timeDifference || inferredResult(b) - inferredResult(a);
  });
  const categoryFor = (entry: typeof historyItems[number]): HistoryEventCategory => (entry.kind === "measurement" || entry.kind === "voltage") ? "measurement" : entry.kind === "transfer" ? "transfer" : entry.item.type === "charge" ? "charge" : entry.item.type === "discharge" ? "discharge" : "manual";
  const fromTime = historyFilter.from ? new Date(`${historyFilter.from}T00:00:00`).getTime() : Number.NEGATIVE_INFINITY;
  const toTime = historyFilter.to ? new Date(`${historyFilter.to}T23:59:59.999`).getTime() : Number.POSITIVE_INFINITY;
  const filteredHistoryItems = historyItems.filter(entry => historyFilter.categories.includes(categoryFor(entry)) && new Date(entry.at).getTime() >= fromTime && new Date(entry.at).getTime() <= toTime);
  const historyFilterActive = historyFilter.categories.length !== historyEventCategories.length || Boolean(historyFilter.from || historyFilter.to);
  return <div className="page detail-page" onTouchStart={startBackGesture} onTouchEnd={finishBackGesture}>
    <Link to={admin ? "/admin/batteries" : "/"} className="back-link"><ArrowLeft size={17}/> {t("battery.overview")}</Link>
    <section className="detail-title"><div className="battery-identity"><span className={`large-battery ${battery.activeSince ? "active" : ""}`}><BatteryMedium/></span><div><span className="battery-crew-line"><CrewIdentity number={battery.crewNumber} name={battery.crewName} color={battery.crewColor}/><small>· {battery.serialNumber}</small></span><h1>{battery.activeSince ? `⚡ ${battery.label}` : battery.label}</h1>{battery.archivedAt ? <p>{t(battery.state === "retired" ? "states.retired" : "common.archived")}</p> : battery.activeSince && <p className="active-battery-label">{t("dashboard.crewActive.inDrone")}</p>}</div></div><div className="hero-actions">{!admin && !battery.archivedAt && battery.state !== "retired" && <button className="button primary" onClick={() => setForm("event")}><Plus size={16}/> {t("events.recordCheck")}</button>}{admin && <button className="button secondary" onClick={() => setForm("edit")}><Pencil size={16}/> {t("common.edit")}</button>}{admin && battery.archivedAt && battery.state !== "retired" && <button className="button secondary" onClick={() => archive.mutate()} disabled={archive.isPending}><RotateCcw size={16}/> {t("battery.restore")}</button>}{superAdmin && <button className="button destructive" disabled={remove.isPending} onClick={() => confirm(t("battery.deleteConfirm", { name: battery.label })) && remove.mutate()}><Trash2 size={16}/> {remove.isPending ? t("battery.deleting") : t("battery.delete")}</button>}</div></section>
    {archive.error && <p className="admin-error">{t("errors.generic")}</p>}
    {remove.error && <p className="admin-error">{t("battery.deleteError")}</p>}
    {measurement?.health === "danger" && <div className="alert-banner"><span>!</span><div><strong>{t("battery.dangerTitle")}</strong><p>{t("battery.dangerText", { delta: measurement.cellDelta.toFixed(2), threshold: measurement.dangerThresholdV.toFixed(2) })}</p></div></div>}
    <section className="primary-battery-metrics" aria-label={t("battery.metrics.operational")}><article className={discharged ? "discharged" : ""}><strong>{charge?.chargePercent != null ? `${charge.chargePercent}%` : "—"}</strong><small>{t("battery.metrics.charge")}</small></article><article><strong>{charge ? charge.totalVoltage.toFixed(2) : "—"} <em>{charge ? t("common.volts") : ""}</em></strong><small>{t("battery.metrics.totalVoltage")}</small></article><article className={measurement?.health === "danger" ? "danger" : measurement?.health === "warning" ? "warning" : ""}><strong>Δ {measurement ? measurement.cellDelta.toFixed(2) : "—"} <em>{measurement ? t("common.volts") : ""}</em></strong><small>{t("battery.metrics.balance")}</small></article></section>
    {battery.latestVoltageEvent && <section className="panel"><h2>{t("battery.voltage.latest")}</h2><p><strong>{battery.latestVoltageEvent.totalVoltage.toFixed(2)} {t("common.volts")}</strong> · {t(`battery.voltage.${battery.latestVoltageEvent.type}`)}</p><small>Mission Planner · {formatDate(battery.latestVoltageEvent.measuredAt)} · {t("battery.voltage.noCells")}</small></section>}
    <div className="detail-grid"><section className="panel cells-panel"><div className="panel-head"><div><h2>{t("battery.cells.title")}</h2><p>{t("battery.cells.subtitle")}</p></div>{measurement && <span className="delta-callout">Δ {measurement.cellDelta.toFixed(2)} {t("common.volts")}</span>}</div>{measurement ? <div className="cell-modules">{cellModules.map(({ module, cells }) => <section className="cell-module" key={module}><div className="cell-module-head"><strong>{t("photos.battery", { module })}</strong><span>{t("events.totalVoltage")}: {cells.reduce((sum, voltage) => sum + voltage, 0).toFixed(2)} {t("common.volts")}</span></div><table className="cell-table"><thead><tr>{cells.map((_, index) => <th key={index}>{module}{index + 1}</th>)}</tr></thead><tbody><tr>{cells.map((voltage, index) => { const abnormal = measurement.maxCellVoltage - voltage >= measurement.warningThresholdV; return <td className={abnormal ? measurement.health : ""} key={index}>{voltage.toFixed(2)}</td>; })}</tr></tbody></table></section>)}</div> : <div className="empty">{t("battery.cells.empty")}</div>}</section>
      <aside className="panel pack-info"><div className="panel-head"><div><h2>{t("battery.info.title")}</h2><p>{t("battery.info.subtitle")}</p></div></div><dl><div><dt>{t("battery.metrics.health")}</dt><dd>{measurement ? <HealthBadge health={measurement.health}/> : "—"}</dd></div><div><dt>{t("battery.info.state")}</dt><dd className={battery.activeSince ? "active-battery-label" : discharged ? "charge-level discharged" : ""}>{battery.activeSince ? t("dashboard.crewActive.inDrone") : t(battery.archivedAt && battery.state !== "retired" ? "common.archived" : discharged ? "states.discharged" : `states.${battery.state}`)}</dd></div><div><dt>{t("battery.metrics.cycleCount")}</dt><dd>{battery.cycleCount}</dd></div><div><dt>{t("common.crew")}</dt><dd><CrewIdentity number={battery.crewNumber} name={battery.crewName} color={battery.crewColor} size="small"/></dd></div><div><dt>{t("forms.batteryType")}</dt><dd>{battery.typeName}</dd></div><div><dt>{t("common.capacity")}</dt><dd><Gauge size={15}/>{battery.capacityAh} {t("common.ampHours")}</dd></div><div><dt>{t("forms.cellCount")}</dt><dd>{battery.cellCount}</dd></div><div><dt>{t("forms.chemistry")}</dt><dd>{battery.chemistry}</dd></div><div><dt>{t("batteryTypes.voltageRange")}</dt><dd>{battery.minVoltage.toFixed(2)}–{battery.maxVoltage.toFixed(2)} {t("common.volts")}</dd></div><div><dt>{t("battery.metrics.latestCheck")}</dt><dd>{measurement ? formatDate(measurement.measuredAt) : t("battery.metrics.noChecks")}</dd></div></dl>{battery.notes && <p className="notes-box">{battery.notes}</p>}</aside>
    </div>
    <section className="panel battery-history"><div className="panel-head"><div><h2>{t("battery.history.title")}</h2><p>{t("battery.history.subtitle", { count: battery.cycleCount })} · {t("battery.history.visible", { visible: filteredHistoryItems.length, total: historyItems.length })}</p></div><div className="history-actions">{admin && battery.state !== "retired" && <button className="button compact" onClick={() => setForm("event")}><Plus size={15}/> {t("battery.history.add")}</button>}<details className="history-filter"><summary className={`icon-button ${historyFilterActive ? "active" : ""}`} aria-label={t("battery.history.filter.title")}><SlidersHorizontal/>{historyFilterActive && <i/>}</summary><div className="history-filter-popover"><div className="history-filter-title"><strong>{t("battery.history.filter.title")}</strong>{historyFilterActive && <button type="button" onClick={() => updateHistoryFilter({ ...defaultHistoryFilter, categories: [...defaultHistoryFilter.categories] })}>{t("battery.history.filter.reset")}</button>}</div><button type="button" className="history-filter-preset" onClick={() => updateHistoryFilter({ ...historyFilter, categories: ["charge", "discharge"] })}>{t("battery.history.filter.chargeOnly")}</button><fieldset><legend>{t("battery.history.filter.events")}</legend>{historyEventCategories.map(category => <label key={category}><input type="checkbox" checked={historyFilter.categories.includes(category)} onChange={event => updateHistoryFilter({ ...historyFilter, categories: event.target.checked ? [...historyFilter.categories, category] : historyFilter.categories.filter(item => item !== category) })}/><span>{t(`battery.history.filter.categories.${category}`)}</span></label>)}</fieldset><fieldset className="history-date-range"><legend>{t("battery.history.filter.period")}</legend><label><span>{t("battery.history.filter.from")}</span><input type="date" value={historyFilter.from} max={historyFilter.to || undefined} onChange={event => updateHistoryFilter({ ...historyFilter, from: event.target.value })}/></label><label><span>{t("battery.history.filter.to")}</span><input type="date" value={historyFilter.to} min={historyFilter.from || undefined} onChange={event => updateHistoryFilter({ ...historyFilter, to: event.target.value })}/></label></fieldset></div></details></div></div><div className="battery-history-table-wrap"><table className="battery-history-table">
      <thead><tr><th>{t("battery.history.date")}</th><th>{t("battery.history.event")}</th><th>{t("events.totalVoltage")}</th><th>{t("battery.history.details")}</th></tr></thead>
      <tbody>{filteredHistoryItems.map(entry => {
        if (entry.kind === "voltage") { const item = entry.item; return <tr key={`voltage-${item.id}`}>
          <td>{formatDate(entry.at)}</td><td>{t(`battery.voltage.${item.type}`)}</td><td>{item.totalVoltage.toFixed(2)} {t("common.volts")}</td>
          <td>Mission Planner · {t("battery.voltage.noCells")}<br/><small>{t("battery.voltage.sampleTime")}: {formatDate(item.measuredAt)}</small></td></tr>; }
        if (entry.kind === "measurement") { const item = entry.item; return <tr key={`measurement-${item.id}`}>
          <td>{formatDate(entry.at)}</td><td>{t("battery.history.measurement")}{item.correctedAt && <small> · {t("common.corrected")}</small>}</td>
          <td>{item.totalVoltage.toFixed(2)} {t("common.volts")}</td><td><span>{item.chargePercent != null ? `${item.chargePercent}% · ` : ""}Δ {item.cellDelta.toFixed(2)} {t("common.volts")} </span><HealthBadge health={item.health}/>
          {item.notes && <p>{item.notes}</p>}{admin && <button className="icon-button mini" onClick={() => setCorrection(item)} aria-label={t("battery.measurements.correct")}><Pencil/></button>}</td></tr>; }
        if (entry.kind === "transfer") { const item = entry.item; return <tr key={`transfer-${item.id}`}><td>{formatDate(entry.at)}</td><td>{t("battery.history.transfer")}</td><td>—</td><td>{item.fromCrewName ?? "—"} → {item.toCrewName}{item.notes && <p>{item.notes}</p>}</td></tr>; }
        const item = entry.item;
        const source = item.sourceMeasurementId ? measurementsById.get(item.sourceMeasurementId) : undefined;
        return <tr key={`event-${item.id}`}><td>{formatDate(entry.at)}</td><td>{t(`cycleTypes.${item.type}`)}</td>
          <td>{source ? `${source.totalVoltage.toFixed(2)} ${t("common.volts")}` : "—"}</td><td>
          {item.type === "charge" && item.inferred && t("battery.history.cycleCompleted", { count: cycleNumbers.get(item.id) ?? battery.cycleCount })}
          {item.type === "discharge" && item.inferred && t("battery.history.usageRecorded")}
          {item.flightMinutes ? ` · ${item.flightMinutes} ${t("common.minutesShort")}` : ""}{item.notes && <p>{item.notes}</p>}</td></tr>;
      })}</tbody></table></div>
      {!historyItems.length ? <div className="empty">{t("battery.history.empty")}</div> : !filteredHistoryItems.length && <div className="empty">{t("battery.history.filter.empty")}</div>}
    </section>
    {form === "edit" && <BatteryForm battery={battery} onClose={() => setForm(null)}/>}
    {form === "event" && <EventForm id={battery.id} batteryLabel={battery.label} crewId={battery.crewId} archived={Boolean(battery.archivedAt)} admin={admin} cellCount={battery.cellCount} minVoltage={battery.minVoltage} maxVoltage={battery.maxVoltage} onClose={() => setForm(null)}/>}
    {correction && <CorrectionForm measurement={correction} minVoltage={battery.minVoltage} maxVoltage={battery.maxVoltage} onClose={() => setCorrection(null)}/>}
  </div>;
}
