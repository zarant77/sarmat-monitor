import { useEffect, useRef, useState, type FormEvent } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Archive, ArrowLeft, ArrowRightLeft, BatteryCharging, BatteryLow, BatteryMedium, CalendarClock, Camera, Check, ClipboardPlus, Gauge, LoaderCircle, MessageSquare, Pencil, Plus, RotateCcw, SearchCheck, SlidersHorizontal, Wrench } from "lucide-react";
import { Link, useParams } from "react-router-dom";
import type { CheckerCombinedPreview, CheckerModule, CheckerModuleRecognition, Measurement } from "@sbm/shared";
import { ApiError, api } from "../api";
import { useAuth } from "../auth";
import { BatteryForm } from "../components/Forms";
import { HealthBadge } from "../components/HealthBadge";
import { Modal } from "../components/Modal";
import { CheckerCamera, type CapturedCheckerImage } from "../components/CheckerCamera";
import { useI18n } from "../i18n";
import { clientSettings, defaultHistoryFilter, historyEventCategories, type HistoryEventCategory, type HistoryFilterSettings } from "../client-settings";

const createPhotoSetId = () => {
  if (typeof crypto.randomUUID === "function") return crypto.randomUUID();
  const bytes = crypto.getRandomValues(new Uint8Array(16));
  bytes[6] = (bytes[6] & 0x0f) | 0x40; bytes[8] = (bytes[8] & 0x3f) | 0x80;
  const hex = [...bytes].map(value => value.toString(16).padStart(2, "0"));
  return `${hex.slice(0, 4).join("")}-${hex.slice(4, 6).join("")}-${hex.slice(6, 8).join("")}-${hex.slice(8, 10).join("")}-${hex.slice(10).join("")}`;
};

function EventForm({ id, cellCount, type, onClose }: { id: string; cellCount: number; type: "check" | "cycle" | "transfer"; onClose: () => void }) {
  const { t } = useI18n(); const qc = useQueryClient();
  const crews = useQuery({ queryKey: ["crews"], queryFn: () => api.crews(), enabled: type === "transfer" });
  const mutation = useMutation({ mutationFn: (data: any) => type === "check" ? api.measurement(id, data) : type === "cycle" ? api.cycle(id, data) : api.transfer(id, data.crewId, data.notes), onSuccess: () => { qc.invalidateQueries({ queryKey: ["battery", id] }); qc.invalidateQueries({ queryKey: ["batteries"] }); qc.invalidateQueries({ queryKey: ["crews"] }); onClose(); } });
  const [cells, setCells] = useState(Array.from({ length: cellCount }, () => ""));
  type PhotoState = { previewUrl: string; status: "processing" | "ready" | "error"; recognition?: CheckerModuleRecognition; error?: string };
  const [photoSetId] = useState(createPhotoSetId); const [cameraModule, setCameraModule] = useState<CheckerModule | null>(null); const [photos, setPhotos] = useState<Partial<Record<CheckerModule, PhotoState>>>({}); const photoUrls = useRef<string[]>([]);
  const [combinedPreview, setCombinedPreview] = useState<CheckerCombinedPreview | null>(null);
  const [previewError, setPreviewError] = useState("");
  const [correctedCells, setCorrectedCells] = useState<Set<number>>(() => new Set());
  const photosReady = photos.A?.status === "ready" && photos.B?.status === "ready";
  const anyPhotoProcessing = photos.A?.status === "processing" || photos.B?.status === "processing";
  const numericCells = cells.map(value => Number(value));
  const cellsComplete = cells.every(value => value !== "" && Number.isFinite(Number(value)) && Number(value) > 0);
  const firstModuleCount = Math.ceil(cellCount / 2);
  const modules: Array<{ module: CheckerModule; start: number; end: number }> = [
    { module: "A", start: 0, end: firstModuleCount },
    { module: "B", start: firstModuleCount, end: cellCount }
  ];
  const updateCell = (index: number, value: string) => {
    setCombinedPreview(null);
    setCorrectedCells(current => new Set(current).add(index));
    setCells(items => items.map((item, itemIndex) => itemIndex === index ? value : item));
  };
  useEffect(() => () => { photoUrls.current.forEach(url => URL.revokeObjectURL(url)); }, []);
  useEffect(() => {
    if (type !== "check" || !photosReady || !cellsComplete || cellCount !== 12) { setCombinedPreview(null); return; }
    const timer = window.setTimeout(() => {
      void api.checkerPreview(id, { photoSetId, A: { cells: numericCells.slice(0, 6) }, B: { cells: numericCells.slice(6, 12) } })
        .then(result => { setCombinedPreview(result); setPreviewError(""); })
        .catch(() => { setCombinedPreview(null); setPreviewError(t("recognition.previewError")); });
    }, 250);
    return () => window.clearTimeout(timer);
  }, [type, photosReady, cellsComplete, cellCount, id, photoSetId, cells.join("|"), t]);
  const recognitionErrorMessage = (error: unknown) => {
    if (error instanceof ApiError && error.code === "CHECKER_RECOGNITION_FAILED") {
      const issues = Array.isArray(error.details) ? error.details as Array<{ code?: string }> : [];
      if (issues.some(issue => issue.code === "provider_unavailable" || issue.code === "model_unavailable")) return error.message;
      return t("recognition.retry");
    }
    return error instanceof Error ? error.message : t("camera.uploadError");
  };
  const queuePhoto = (module: CheckerModule, image: CapturedCheckerImage) => {
    const previewUrl = URL.createObjectURL(image.blob);
    photoUrls.current.push(previewUrl);
    setPhotos(current => {
      if (current[module]) {
        URL.revokeObjectURL(current[module]!.previewUrl);
        photoUrls.current = photoUrls.current.filter(url => url !== current[module]!.previewUrl);
      }
      return { ...current, [module]: { previewUrl, status: "processing" } };
    });
    setCameraModule(null);
    const start = module === "A" ? 0 : 6;
    setCombinedPreview(null);
    setPreviewError("");
    void api.uploadCheckerImage(id, photoSetId, module, image.blob, image.width, image.height).then(uploaded => {
      setPhotos(current => current[module]?.previewUrl === previewUrl ? { ...current, [module]: { previewUrl, status: "ready", recognition: uploaded.recognition } } : current);
      setCorrectedCells(current => { const next = new Set(current); for (let index = start; index < start + 6; index += 1) next.delete(index); return next; });
      setCells(current => current.map((value, index) => {
        if (index < start || index >= start + 6) return value;
        const recognized = uploaded.recognition.cells[index - start].voltage;
        return recognized == null ? "" : recognized.toFixed(2);
      }));
    }).catch(error => {
      const message = recognitionErrorMessage(error);
      setPhotos(current => current[module]?.previewUrl === previewUrl ? { ...current, [module]: { previewUrl, status: "error", error: message } } : current);
    });
  };
  const submit = (event: FormEvent<HTMLFormElement>) => { event.preventDefault(); const data = new FormData(event.currentTarget); if (type === "check") mutation.mutate({ cellVoltages: cells.map(Number), notes: data.get("notes"), photoSetId }); else if (type === "cycle") mutation.mutate({ type: data.get("type"), notes: data.get("notes") }); else mutation.mutate({ crewId: data.get("crewId"), notes: data.get("notes") }); };
  const title = type === "check" ? t("events.recordCheck") : type === "cycle" ? t("events.addUsage") : t("events.transferBattery");
  return <Modal title={title} eyebrow={t("events.history")} onClose={onClose}><form onSubmit={submit} className="form-grid">
    {type === "check" && <div className="checker-check-layout full">
      {modules.map(({ module, start, end }) => <section className="checker-module" key={module}>
        <article className={`checker-photo-card ${photos[module]?.status ?? ""} ${photos[module] ? "added" : ""}`}>
          {photos[module] ? <img src={photos[module]!.previewUrl} alt={t("photos.preview", { module })}/> : <span className="checker-photo-placeholder"><Camera/></span>}
          <div><strong>{t("photos.battery", { module })}</strong><small>{photos[module]?.status === "processing" ? <><LoaderCircle className="spin"/> {t("recognition.processing")}</> : photos[module]?.status === "error" ? <span className="recognition-error">{photos[module]!.error}</span> : photos[module]?.recognition ? <><Check/> {photos[module]!.recognition!.complete ? t("recognition.recognized") : t("recognition.partial", { count: photos[module]!.recognition!.cells.filter(cell => cell.voltage == null).length })}</> : t("photos.required")}</small></div>
          <button type="button" disabled={photos[module]?.status === "processing"} className={`button ${photos[module] ? "secondary" : "primary"}`} onClick={() => setCameraModule(module)}>{photos[module]?.status === "processing" ? <LoaderCircle className="spin"/> : <Camera/>} {photos[module]?.status === "processing" ? t("recognition.processingShort") : photos[module]?.status === "error" ? t("photos.retry") : photos[module] ? t("photos.replace") : t("photos.take", { module })}</button>
        </article>
        <div className="checker-cell-row" style={{ gridTemplateColumns: `repeat(${Math.max(1, end - start)}, minmax(0, 1fr))` }}>
          {cells.slice(start, end).map((value, offset) => { const index = start + offset; const recognition = photos[module]?.recognition?.cells[offset]; const uncertain = Boolean(recognition && (recognition.voltage == null || recognition.confidence !== "high") && !correctedCells.has(index)); return <label className={uncertain ? "uncertain" : ""} key={index}><span>{index + 1}{uncertain ? " !" : ""}</span><input aria-label={`${t("common.cell")} ${index + 1}`} disabled={photos[module]?.status === "processing"} inputMode="decimal" type="number" step="0.01" min="0" placeholder={photos[module]?.status === "processing" ? "…" : uncertain ? "?" : undefined} value={value} required onChange={event => updateCell(index, event.target.value)}/></label>; })}
        </div>
      </section>)}
      <div className="checker-calculated">
        <span><small>{t("events.totalVoltage")}</small><strong>{combinedPreview ? combinedPreview.combinedTotalVoltage.toFixed(2) : "—"} <em>{t("common.volts")}</em></strong></span>
        <span><small>{t("events.chargePercent")}</small><strong>{combinedPreview?.chargePercent ?? "—"}{combinedPreview?.chargePercent != null && <em>%</em>}</strong></span>
      </div>
      {combinedPreview && <div className="recognition-preview"><span><small>A</small><strong>{combinedPreview.moduleATotalVoltage.toFixed(2)} {t("common.volts")}</strong></span><span><small>B</small><strong>{combinedPreview.moduleBTotalVoltage.toFixed(2)} {t("common.volts")}</strong></span><span><small>{t("recognition.minMax")}</small><strong>{combinedPreview.minCellVoltage.toFixed(2)}–{combinedPreview.maxCellVoltage.toFixed(2)}</strong></span><span><small>Δ</small><strong>{combinedPreview.cellDelta.toFixed(2)} {t("common.volts")}</strong></span><HealthBadge health={combinedPreview.health}/></div>}
      {previewError && <p className="form-error">{previewError}</p>}
    </div>}
    {type === "cycle" && <label>{t("events.eventType")}<select name="type"><option value="maintenance">{t("cycleTypes.maintenance")}</option><option value="repair">{t("cycleTypes.repair")}</option><option value="inspection">{t("cycleTypes.inspection")}</option><option value="service">{t("cycleTypes.service")}</option><option value="retirement">{t("cycleTypes.retirement")}</option><option value="note">{t("cycleTypes.note")}</option></select></label>}
    {type === "transfer" && <label className="full">{t("events.newCrew")}<select name="crewId" required><option value="">{t("events.selectCrew")}</option>{crews.data?.map(crew => <option value={crew.id} key={crew.id}>№{crew.number} · {crew.name}</option>)}</select></label>}
    <label className="full">{t("common.notes")}<textarea name="notes"/></label>{mutation.error && <p className="form-error">{t("errors.generic")}</p>}<div className="form-actions full"><button type="button" className="button secondary" onClick={onClose}>{t("common.cancel")}</button><button className="button primary" disabled={mutation.isPending || (type === "check" && (!photosReady || !combinedPreview))}>{t("events.save")}</button></div>
    {type === "check" && anyPhotoProcessing && <p className="photo-processing-note full"><LoaderCircle className="spin"/>{t("recognition.backgroundHelp")}</p>}
    {type === "check" && !anyPhotoProcessing && !photosReady && <p className="photo-required-note full">{t("photos.bothRequired")}</p>}
  </form>{cameraModule && <CheckerCamera module={cameraModule} onCancel={() => setCameraModule(null)} onConfirm={image => queuePhoto(cameraModule, image)}/>}</Modal>;
}

function CorrectionForm({ measurement, minVoltage, maxVoltage, onClose }: { measurement: Measurement; minVoltage: number; maxVoltage: number; onClose: () => void }) {
  const { t } = useI18n(); const qc = useQueryClient(); const [cells, setCells] = useState(measurement.cellVoltages.map(voltage => voltage.toFixed(2)));
  const mutation = useMutation({ mutationFn: (data: any) => api.correctMeasurement(measurement.id, data), onSuccess: () => { qc.invalidateQueries({ queryKey: ["battery", measurement.batteryId] }); onClose(); } });
  const totalVoltage = cells.reduce((sum, value) => sum + Number(value), 0);
  const chargePercent = Math.round(Math.max(0, Math.min(100, (totalVoltage - minVoltage) / (maxVoltage - minVoltage) * 100)));
  const submit = (event: FormEvent<HTMLFormElement>) => { event.preventDefault(); const data = new FormData(event.currentTarget); mutation.mutate({ cellVoltages: cells.map(Number), notes: String(data.get("notes")) }); };
  return <Modal title={t("events.correctMeasurement")} eyebrow={t("events.adminAction")} onClose={onClose}><form className="form-grid" onSubmit={submit}>
    <div className="checker-calculated full"><span><small>{t("events.totalVoltage")}</small><strong>{totalVoltage.toFixed(2)} <em>{t("common.volts")}</em></strong></span><span><small>{t("events.chargePercent")}</small><strong>{chargePercent}<em>%</em></strong></span></div>
    <div className="full"><span className="label-text">{t("events.cellsV")}</span><div className="cell-input-grid">{cells.map((value, index) => <label key={index}><span>{index + 1}</span><input type="number" step="0.01" value={value} required onChange={event => setCells(items => items.map((item, itemIndex) => itemIndex === index ? event.target.value : item))}/></label>)}</div></div>
    <label className="full">{t("events.correctionNote")}<textarea name="notes" defaultValue={measurement.notes}/></label>{mutation.error && <p className="form-error">{t("errors.generic")}</p>}<div className="form-actions full"><button type="button" className="button secondary" onClick={onClose}>{t("common.cancel")}</button><button className="button primary">{t("events.saveCorrection")}</button></div>
  </form></Modal>;
}

export function BatteryDetails() {
  const { id = "" } = useParams(); const auth = useAuth(); const { t, locale } = useI18n(); const admin = auth.user?.role !== "CREW"; const qc = useQueryClient();
  const query = useQuery({ queryKey: ["battery", id], queryFn: () => api.battery(id) });
  const [form, setForm] = useState<"check" | "cycle" | "transfer" | "edit" | null>(null); const [correction, setCorrection] = useState<Measurement | null>(null); const battery = query.data;
  const [historyFilter, setHistoryFilter] = useState<HistoryFilterSettings>(() => clientSettings.getHistoryFilter());
  const updateHistoryFilter = (next: HistoryFilterSettings) => { setHistoryFilter(next); clientSettings.setHistoryFilter(next); };
  const archive = useMutation({ mutationFn: () => battery?.archivedAt ? api.restoreBattery(id) : api.archiveBattery(id), onSuccess: () => { qc.invalidateQueries({ queryKey: ["battery", id] }); qc.invalidateQueries({ queryKey: ["batteries"] }); } });
  const formatDate = (value: string) => new Intl.DateTimeFormat(locale === "uk" ? "uk-UA" : "en-US", { dateStyle: "medium", timeStyle: "short" }).format(new Date(value));
  if (query.isLoading) return <div className="page"><div className="empty">{t("battery.loading")}</div></div>;
  if (!battery) return <div className="page"><Link to={admin ? "/admin/batteries" : "/"} className="back-link"><ArrowLeft/> {t("battery.back")}</Link><div className="empty error">{t("battery.loadError")}</div></div>;
  const measurement = battery.latestMeasurement;
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
    ...battery.measurements.map(item => ({ kind: "measurement" as const, at: item.measuredAt, item })),
    ...battery.cycleEvents.map(item => ({ kind: "event" as const, at: item.occurredAt, item })),
    ...battery.transfers.map(item => ({ kind: "transfer" as const, at: item.transferredAt, item }))
  ].sort((a, b) => new Date(b.at).getTime() - new Date(a.at).getTime());
  const categoryFor = (entry: typeof historyItems[number]): HistoryEventCategory => entry.kind === "measurement" ? "measurement" : entry.kind === "transfer" ? "transfer" : entry.item.type === "charge" ? "charge" : entry.item.type === "discharge" ? "discharge" : "manual";
  const fromTime = historyFilter.from ? new Date(`${historyFilter.from}T00:00:00`).getTime() : Number.NEGATIVE_INFINITY;
  const toTime = historyFilter.to ? new Date(`${historyFilter.to}T23:59:59.999`).getTime() : Number.POSITIVE_INFINITY;
  const filteredHistoryItems = historyItems.filter(entry => historyFilter.categories.includes(categoryFor(entry)) && new Date(entry.at).getTime() >= fromTime && new Date(entry.at).getTime() <= toTime);
  const historyFilterActive = historyFilter.categories.length !== historyEventCategories.length || Boolean(historyFilter.from || historyFilter.to);
  return <div className="page detail-page">
    <Link to={admin ? "/admin/batteries" : "/"} className="back-link"><ArrowLeft size={17}/> {t("battery.overview")}</Link>
    <section className="detail-title"><div className="battery-identity"><span className="large-battery"><BatteryMedium/></span><div><span className="eyebrow"><i className="crew-color-dot" style={{ backgroundColor: battery.crewColor }}/>№{battery.crewNumber} · {battery.crewName} · {battery.serialNumber}</span><h1>{battery.label}</h1>{battery.archivedAt && <p>{t("common.archived")}</p>}</div></div><div className="hero-actions">{admin && <button className="button secondary" onClick={() => setForm("edit")}><Pencil size={16}/> {t("common.edit")}</button>}{admin && <button className="button secondary" onClick={() => setForm("transfer")}><ArrowRightLeft size={16}/> {t("battery.transfer")}</button>}{admin && <button className="button secondary" onClick={() => archive.mutate()}>{battery.archivedAt ? <RotateCcw size={16}/> : <Archive size={16}/>} {battery.archivedAt ? t("battery.restore") : t("battery.archive")}</button>}<button className="button primary" onClick={() => setForm("check")} disabled={Boolean(battery.archivedAt)}><ClipboardPlus size={17}/> {t("battery.recordCheck")}</button></div></section>
    {measurement?.health === "danger" && <div className="alert-banner"><span>!</span><div><strong>{t("battery.dangerTitle")}</strong><p>{t("battery.dangerText", { delta: measurement.cellDelta.toFixed(2), threshold: measurement.dangerThresholdV.toFixed(2) })}</p></div></div>}
    <div className="detail-grid"><section className="panel cells-panel"><div className="panel-head"><div><h2>{t("battery.cells.title")}</h2><p>{t("battery.cells.subtitle")}</p></div>{measurement && <span className="delta-callout">Δ {measurement.cellDelta.toFixed(2)} {t("common.volts")}</span>}</div>{measurement ? <div className="cell-modules">{cellModules.map(({ module, cells }) => <section className="cell-module" key={module}><div className="cell-module-head"><strong>{t("photos.battery", { module })}</strong><span>{t("events.totalVoltage")}: {cells.reduce((sum, voltage) => sum + voltage, 0).toFixed(2)} {t("common.volts")}</span></div><table className="cell-table"><thead><tr>{cells.map((_, index) => <th key={index}>{module}{index + 1}</th>)}</tr></thead><tbody><tr>{cells.map((voltage, index) => { const abnormal = measurement.maxCellVoltage - voltage >= measurement.warningThresholdV; return <td className={abnormal ? measurement.health : ""} key={index}>{voltage.toFixed(2)}</td>; })}</tr></tbody></table></section>)}</div> : <div className="empty">{t("battery.cells.empty")}</div>}</section>
      <aside className="panel pack-info"><div className="panel-head"><div><h2>{t("battery.info.title")}</h2><p>{t("battery.info.subtitle")}</p></div></div><dl><div><dt>{t("battery.metrics.health")}</dt><dd>{measurement ? <HealthBadge health={measurement.health}/> : "—"}</dd></div><div><dt>{t("battery.metrics.charge")}</dt><dd>{measurement?.chargePercent != null ? `${measurement.chargePercent}%` : "—"}</dd></div><div><dt>{t("battery.metrics.totalVoltage")}</dt><dd>{measurement ? `${measurement.totalVoltage.toFixed(2)} ${t("common.volts")}` : "—"}</dd></div><div><dt>{t("battery.metrics.cycleCount")}</dt><dd>{battery.cycleCount}</dd></div><div><dt>{t("common.crew")}</dt><dd><i className="crew-color-dot" style={{ backgroundColor: battery.crewColor }}/>№{battery.crewNumber} · {battery.crewName}</dd></div><div><dt>{t("forms.batteryType")}</dt><dd>{battery.typeName}</dd></div><div><dt>{t("battery.info.state")}</dt><dd>{t(`states.${battery.state}`)}</dd></div><div><dt>{t("common.capacity")}</dt><dd><Gauge size={15}/>{battery.capacityAh} {t("common.ampHours")}</dd></div><div><dt>{t("forms.cellCount")}</dt><dd>{battery.cellCount}</dd></div><div><dt>{t("forms.chemistry")}</dt><dd>{battery.chemistry}</dd></div><div><dt>{t("batteryTypes.voltageRange")}</dt><dd>{battery.minVoltage.toFixed(2)}–{battery.maxVoltage.toFixed(2)} {t("common.volts")}</dd></div><div><dt>{t("battery.metrics.latestCheck")}</dt><dd>{measurement ? formatDate(measurement.measuredAt) : t("battery.metrics.noChecks")}</dd></div></dl>{battery.notes && <p className="notes-box">{battery.notes}</p>}</aside>
    </div>
    <section className="panel battery-history"><div className="panel-head"><div><h2>{t("battery.history.title")}</h2><p>{t("battery.history.subtitle", { count: battery.cycleCount })} · {t("battery.history.visible", { visible: filteredHistoryItems.length, total: historyItems.length })}</p></div><div className="history-actions">{admin && <button className="button compact" onClick={() => setForm("cycle")}><Plus size={15}/> {t("battery.history.add")}</button>}<details className="history-filter"><summary className={`icon-button ${historyFilterActive ? "active" : ""}`} aria-label={t("battery.history.filter.title")}><SlidersHorizontal/>{historyFilterActive && <i/>}</summary><div className="history-filter-popover"><div className="history-filter-title"><strong>{t("battery.history.filter.title")}</strong>{historyFilterActive && <button type="button" onClick={() => updateHistoryFilter({ ...defaultHistoryFilter, categories: [...defaultHistoryFilter.categories] })}>{t("battery.history.filter.reset")}</button>}</div><button type="button" className="history-filter-preset" onClick={() => updateHistoryFilter({ ...historyFilter, categories: ["charge", "discharge"] })}>{t("battery.history.filter.chargeOnly")}</button><fieldset><legend>{t("battery.history.filter.events")}</legend>{historyEventCategories.map(category => <label key={category}><input type="checkbox" checked={historyFilter.categories.includes(category)} onChange={event => updateHistoryFilter({ ...historyFilter, categories: event.target.checked ? [...historyFilter.categories, category] : historyFilter.categories.filter(item => item !== category) })}/><span>{t(`battery.history.filter.categories.${category}`)}</span></label>)}</fieldset><fieldset className="history-date-range"><legend>{t("battery.history.filter.period")}</legend><label><span>{t("battery.history.filter.from")}</span><input type="date" value={historyFilter.from} max={historyFilter.to || undefined} onChange={event => updateHistoryFilter({ ...historyFilter, from: event.target.value })}/></label><label><span>{t("battery.history.filter.to")}</span><input type="date" value={historyFilter.to} min={historyFilter.from || undefined} onChange={event => updateHistoryFilter({ ...historyFilter, to: event.target.value })}/></label></fieldset></div></details></div></div><div className="unified-timeline">{filteredHistoryItems.map(entry => {
      if (entry.kind === "measurement") { const item = entry.item; return <article className={`history-entry measurement ${item.health}`} key={`measurement-${item.id}`}><span className="history-icon"><Gauge/></span><div className="history-body"><strong>{t("battery.history.measurement")}</strong><p>{item.totalVoltage.toFixed(2)} {t("common.volts")} · {item.chargePercent != null ? `${item.chargePercent}%` : "—"} · Δ {item.cellDelta.toFixed(2)} {t("common.volts")}</p><small>{formatDate(item.measuredAt)} {item.correctedAt && `· ${t("common.corrected")}`} {item.notes && `· ${item.notes}`}</small></div><HealthBadge health={item.health}/>{admin && <button className="icon-button mini" onClick={() => setCorrection(item)} aria-label={t("battery.measurements.correct")}><Pencil/></button>}</article>; }
      if (entry.kind === "transfer") { const item = entry.item; return <article className="history-entry transfer" key={`transfer-${item.id}`}><span className="history-icon"><ArrowRightLeft/></span><div className="history-body"><strong>{t("battery.history.transfer")}</strong><p>{item.fromCrewName ?? "—"} → {item.toCrewName}</p><small>{formatDate(item.transferredAt)} {item.notes && `· ${item.notes}`}</small></div></article>; }
      const item = entry.item; const source = item.sourceMeasurementId ? measurementsById.get(item.sourceMeasurementId) : undefined; const icon = item.type === "charge" ? <BatteryCharging/> : item.type === "discharge" ? <BatteryLow/> : item.type === "repair" || item.type === "maintenance" || item.type === "service" ? <Wrench/> : item.type === "inspection" ? <SearchCheck/> : item.type === "note" ? <MessageSquare/> : <CalendarClock/>; return <article className={`history-entry event ${item.type}`} key={`event-${item.id}`}><span className="history-icon">{icon}</span><div className="history-body"><strong>{t(`cycleTypes.${item.type}`)}</strong>{source && <p>{source.totalVoltage.toFixed(2)} {t("common.volts")} · {source.chargePercent != null ? `${source.chargePercent}%` : "—"}</p>}<small>{formatDate(item.occurredAt)} {item.type === "charge" && item.inferred && `· ${t("battery.history.cycleCompleted", { count: cycleNumbers.get(item.id) ?? battery.cycleCount })}`} {item.type === "discharge" && item.inferred && `· ${t("battery.history.usageRecorded")}`} {item.flightMinutes ? `· ${item.flightMinutes} ${t("common.minutesShort")}` : ""} {item.notes && `· ${item.notes}`}</small></div></article>;
    })}{!historyItems.length ? <div className="empty">{t("battery.history.empty")}</div> : !filteredHistoryItems.length && <div className="empty">{t("battery.history.filter.empty")}</div>}</div></section>
    {form === "edit" && <BatteryForm battery={battery} onClose={() => setForm(null)}/>} {form && form !== "edit" && <EventForm id={battery.id} cellCount={battery.cellCount} type={form} onClose={() => setForm(null)}/>} {correction && <CorrectionForm measurement={correction} minVoltage={battery.minVoltage} maxVoltage={battery.maxVoltage} onClose={() => setCorrection(null)}/>} 
  </div>;
}
