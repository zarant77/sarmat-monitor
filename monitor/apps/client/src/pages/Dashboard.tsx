import { useRef, useState, type TouchEvent } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { AlertTriangle, BatteryMedium, ChevronDown, Gauge, Pencil, Plus, Users } from "lucide-react";
import { Link, useParams, useSearchParams } from "react-router-dom";
import { api } from "../api";
import { useAuth } from "../auth";
import { HealthBadge } from "../components/HealthBadge";
import { CrewIdentity } from "../components/CrewIdentity";
import { BatteryForm, CrewForm } from "../components/Forms";
import { useI18n } from "../i18n";
import { clientSettings } from "../client-settings";
import { DEFAULT_DISCHARGED_THRESHOLD_PERCENT, batteryCharge, isBatteryDischarged } from "../battery-display";
import { classifyGesture, isCompletedHorizontalSwipe, type GestureDirection } from "../swipe-gesture";

function CrewBatteryCard({ battery, discharged, busy, pending, onToggle }: { battery: Awaited<ReturnType<typeof api.batteries>>[number]; discharged: boolean; busy: boolean; pending: boolean; onToggle: (id: string) => void }) {
  const { t } = useI18n();
  const [offset, setOffset] = useState(0);
  const gesture = useRef<{ startX: number; startY: number; direction: GestureDirection }>({ startX: 0, startY: 0, direction: "undecided" });
  const suppressClick = useRef(false);
  const measurement = battery.latestMeasurement; const charge = batteryCharge(battery);
  const active = Boolean(battery.activeSince);
  const start = (event: TouchEvent) => {
    const touch = event.touches[0];
    gesture.current = { startX: touch.clientX, startY: touch.clientY, direction: "undecided" };
    suppressClick.current = false;
  };
  const move = (event: TouchEvent) => {
    const touch = event.touches[0];
    const dx = touch.clientX - gesture.current.startX;
    const dy = touch.clientY - gesture.current.startY;
    if (gesture.current.direction === "undecided") gesture.current.direction = classifyGesture(dx, dy);
    if (gesture.current.direction === "horizontal") {
      event.preventDefault();
      setOffset(Math.max(-180, Math.min(180, dx)));
    }
  };
  const finish = (event: TouchEvent) => {
    const touch = event.changedTouches[0];
    const dx = touch.clientX - gesture.current.startX;
    const dy = touch.clientY - gesture.current.startY;
    const toggle = gesture.current.direction === "horizontal" && isCompletedHorizontalSwipe(dx, dy);
    suppressClick.current = gesture.current.direction === "horizontal";
    setOffset(0);
    if (toggle && !busy) onToggle(battery.id);
  };
  return <div className={`crew-battery-swipe ${active ? "active" : ""} ${pending ? "pending" : ""}`}>
    <span className="crew-swipe-action start">{active ? t("dashboard.crewActive.remove") : t("dashboard.crewActive.activate")}</span>
    <span className="crew-swipe-action end">{active ? t("dashboard.crewActive.remove") : t("dashboard.crewActive.activate")}</span>
    <Link to={`/batteries/${battery.id}`} className={`crew-battery-card ${measurement?.health ?? "unchecked"} ${active ? "active" : ""}`} style={{ transform: `translateX(${offset}px)` }} onTouchStart={start} onTouchMove={move} onTouchEnd={finish} onTouchCancel={() => setOffset(0)} onClick={event => { if (suppressClick.current) { event.preventDefault(); suppressClick.current = false; } }}>
      <span className="battery-avatar"><BatteryMedium/></span>
      <div className="crew-battery-main"><strong>{active ? `⚡ ${battery.label}` : battery.label}</strong><small>{battery.serialNumber}</small></div>
      <span className={`crew-battery-state ${active ? "active" : ""}`}><i className={`state-dot ${active ? "active" : discharged ? "discharged" : battery.state}`}/>{active ? t("dashboard.crewActive.inDrone") : t(battery.archivedAt && battery.state !== "retired" ? "common.archived" : discharged ? "states.discharged" : `states.${battery.state}`)}</span>
      <div className="crew-battery-facts">
        <span><small>{t("dashboard.columns.charge")}</small><strong className={discharged ? "charge-level discharged" : "charge-level"}>{charge?.chargePercent != null ? `${charge.chargePercent}%` : "—"}</strong></span>
        <span><small>{t("dashboard.columns.capacity")}</small><strong>{battery.capacityAh} {t("common.ampHours")}</strong></span>
        <span><small>{t("dashboard.columns.voltage")}</small><strong>{charge ? `${charge.totalVoltage.toFixed(2)} ${t("common.volts")}` : "—"}</strong></span>
        <span><small>{t("dashboard.columns.delta")}</small><strong>{measurement ? `${measurement.cellDelta.toFixed(2)} ${t("common.volts")}` : "—"}</strong></span>
      </div>
      {measurement && <HealthBadge health={measurement.health}/>}<ChevronDown className="crew-card-arrow"/>
    </Link>
  </div>;
}

export function Dashboard({ adminMode = false }: { adminMode?: boolean }) {
  const auth = useAuth(); const { t, locale } = useI18n(); const { crewId: routeCrewId } = useParams();
  const qc = useQueryClient();
  const [searchParams, setSearchParams] = useSearchParams();
  const crews = useQuery({ queryKey: ["crews", auth.user?.groupId], queryFn: () => api.crews(), enabled: adminMode });
  const [crewId, setCrewId] = useState(() => clientSettings.getCrewId());
  const [crewForm, setCrewForm] = useState<"create" | "edit" | null>(null); const [batteryForm, setBatteryForm] = useState(false);
  const activeCrewId = routeCrewId ?? (adminMode ? crewId : undefined);
  const batteries = useQuery({ queryKey: ["batteries", activeCrewId ?? "mine", adminMode], queryFn: () => api.batteries(activeCrewId === "all" ? undefined : activeCrewId, adminMode) });
  const toggleActive = useMutation({
    mutationFn: (id: string) => api.toggleActiveBattery(id),
    onMutate: async id => {
      await qc.cancelQueries({ queryKey: ["batteries", activeCrewId ?? "mine", adminMode] });
      const key = ["batteries", activeCrewId ?? "mine", adminMode] as const;
      const previous = qc.getQueryData<Awaited<ReturnType<typeof api.batteries>>>(key);
      const removing = Boolean(previous?.find(item => item.id === id)?.activeSince);
      qc.setQueryData<Awaited<ReturnType<typeof api.batteries>>>(key, current => current?.map(item => ({ ...item, activeSince: !removing && item.id === id ? new Date().toISOString() : null })));
      return { previous, key };
    },
    onError: (_error, _id, context) => { if (context?.previous) qc.setQueryData(context.key, context.previous); },
    onSettled: () => qc.invalidateQueries({ queryKey: ["batteries"] })
  });
  const thresholds = useQuery({ queryKey: ["thresholds"], queryFn: api.thresholds });
  const selectedCrew = crews.data?.find(crew => crew.id === activeCrewId);
  const changeCrew = (id: string) => { setCrewId(id); clientSettings.setCrewId(id); };
  const items = batteries.data ?? [];
  const dischargedThreshold = thresholds.data?.dischargedThresholdPercent ?? DEFAULT_DISCHARGED_THRESHOLD_PERCENT;
  const discharged = (battery: typeof items[number]) => isBatteryDischarged(battery, dischargedThreshold);
  const needsAttention = (battery: typeof items[number]) => discharged(battery) || Boolean(battery.latestMeasurement && battery.latestMeasurement.health !== "good");
  const attention = items.filter(needsAttention).length;
  const attentionOnly = adminMode && searchParams.get("attention") === "1";
  const visibleItems = attentionOnly ? items.filter(needsAttention) : items;
  const totalCapacity = items.reduce((sum, battery) => sum + battery.capacityAh, 0);
  const formattedCapacity = totalCapacity.toLocaleString(locale === "uk" ? "uk-UA" : "en-US");

  if (!adminMode) return <div className="page crew-batteries-page">
    <header className="crew-page-title">{auth.user?.crewNumber != null && auth.user.crewName && auth.user.crewColor && <CrewIdentity number={auth.user.crewNumber} name={auth.user.crewName} color={auth.user.crewColor}/>}<h1>{t("nav.batteries")}</h1></header>
    <section className="crew-summary" aria-label={t("dashboard.crewSummary.label")}>
      <div><small>{t("dashboard.crewSummary.count")}</small><strong>{items.length}</strong></div>
      <div><small>{t("dashboard.crewSummary.capacity")}</small><strong>{formattedCapacity}<em>{t("common.ampHours")}</em></strong></div>
      <div className={attention ? "attention" : ""}><small>{t("dashboard.crewSummary.attention")}</small><strong>{attention}</strong></div>
    </section>
    {toggleActive.isError && <div className="crew-action-error" role="alert">{t("dashboard.crewActive.error")}</div>}
    {batteries.isLoading ? <div className="empty">{t("dashboard.loading")}</div> : batteries.isError ? <div className="empty error">{t("dashboard.serverError")}</div> : !items.length ? <div className="empty crew-empty">{t("dashboard.empty")}</div> : <section className="crew-battery-list">{items.map(battery => <CrewBatteryCard key={battery.id} battery={battery} discharged={discharged(battery)} busy={toggleActive.isPending} pending={toggleActive.isPending && toggleActive.variables === battery.id} onToggle={id => toggleActive.mutate(id)}/>)}</section>}
  </div>;

  return <div className="page dashboard-page">
    <section className="hero-row"><div><span className="eyebrow">{t("dashboard.eyebrow")}</span><h1>{t("dashboard.title")}</h1><p>{t("dashboard.adminDescription")}</p></div><div className="hero-actions">{auth.user?.role === "GROUP_ADMIN"&&<button className="button secondary" onClick={() => setCrewForm("create")}><Users size={17}/> {t("dashboard.newCrew")}</button>}<button className="button primary" onClick={() => setBatteryForm(true)} disabled={!activeCrewId || activeCrewId === "all"}><Plus size={17}/> {t("dashboard.addBattery")}</button></div></section>
    {!routeCrewId && <section className="crew-strip" aria-label={t("dashboard.crewSwitcher")}><button className={`crew-card ${crewId === "all" ? "selected" : ""}`} onClick={() => changeCrew("all")}><span className="crew-symbol">{t("dashboard.all")}</span><span><strong>{t("dashboard.allCrews")}</strong><small>{t("dashboard.packs", { count: crews.data?.reduce((sum, crew) => sum + crew.batteryCount, 0) ?? 0 })}</small></span></button>{crews.data?.map(crew => <button key={crew.id} className={`crew-card ${crewId === crew.id ? "selected" : ""}`} onClick={() => changeCrew(crew.id)}><span><CrewIdentity number={crew.number} name={crew.name} color={crew.color} size="small"/><small>{t("dashboard.packs", { count: crew.batteryCount })}</small></span></button>)}</section>}
    <section className="metrics-grid"><article><span className="metric-icon"><BatteryMedium/></span><div><small>{t("dashboard.packsInView")}</small><strong>{items.length}</strong><p>{t("dashboard.readyForMission", { count: items.filter(battery => battery.state === "ready" && !discharged(battery)).length })}</p></div></article><article><span className="metric-icon"><Gauge/></span><div><small>{t("dashboard.fleetCapacity")}</small><strong>{formattedCapacity} <em>{t("common.ampHours")}</em></strong><p>{t("dashboard.assignedCapacity")}</p></div></article><button type="button" className={`metric-filter ${attention ? "danger-metric" : ""} ${attentionOnly ? "selected" : ""}`} aria-pressed={attentionOnly} onClick={() => setSearchParams(current => { const next = new URLSearchParams(current); attentionOnly ? next.delete("attention") : next.set("attention", "1"); return next; })}><span className="metric-icon"><AlertTriangle/></span><div><small>{t("dashboard.requiresAction")}</small><strong>{attention}</strong><p>{attentionOnly ? t("dashboard.showAll") : attention ? t("dashboard.filterAttention") : t("dashboard.noCritical")}</p></div></button></section>
    <section className="panel fleet-panel"><div className="panel-head"><div><h2>{selectedCrew ? <CrewIdentity number={selectedCrew.number} name={selectedCrew.name} color={selectedCrew.color}/> : t("dashboard.batteryFleet")}</h2><p>{selectedCrew ? t("dashboard.packs", { count: selectedCrew.batteryCount }) : t("dashboard.allAssigned")}</p></div>{selectedCrew && <button className="icon-button" aria-label={t("dashboard.editCrew")} onClick={() => setCrewForm("edit")}><Pencil size={17}/></button>}</div>
      {batteries.isLoading ? <div className="empty">{t("dashboard.loading")}</div> : batteries.isError ? <div className="empty error">{t("dashboard.serverError")}</div> : !visibleItems.length ? <div className="empty">{attentionOnly ? t("dashboard.noAttention") : t("dashboard.empty")}</div> : <div className="battery-table-wrap"><table className="battery-table"><thead><tr><th>{t("dashboard.columns.battery")}</th><th>{t("dashboard.columns.state")}</th><th>{t("dashboard.columns.charge")}</th><th>{t("dashboard.columns.capacity")}</th><th>{t("dashboard.columns.cycles")}</th><th>{t("dashboard.columns.voltage")}</th><th>{t("dashboard.columns.delta")}</th><th aria-label={t("common.open")}/></tr></thead><tbody>{visibleItems.map(battery => {
        const measurement = battery.latestMeasurement; const charge = batteryCharge(battery); const path = `/admin/batteries/${battery.id}`; const isDischarged = discharged(battery);
        return <tr key={battery.id} className={measurement?.health === "danger" ? "danger-row" : measurement?.health === "warning" ? "warning-row" : isDischarged ? "discharged-row" : ""}><td><Link to={path}><span className="battery-avatar"><BatteryMedium/></span><span><strong>{battery.label}</strong><small>{battery.serialNumber} · <CrewIdentity number={battery.crewNumber} name={battery.crewName} color={battery.crewColor} size="small"/></small></span></Link></td><td><span className={`state-dot ${isDischarged ? "discharged" : battery.state}`}/>{t(battery.archivedAt && battery.state !== "retired" ? "common.archived" : isDischarged ? "states.discharged" : `states.${battery.state}`)}</td><td><span className={isDischarged ? "charge-level discharged" : "charge-level"}>{charge?.chargePercent != null ? `${charge.chargePercent}%` : "—"}</span></td><td data-label={`${t("dashboard.columns.capacity")} · `}><strong>{battery.capacityAh}</strong> {t("common.ampHours")}</td><td data-label={`${t("dashboard.columns.cycles")} · `}>{battery.cycleCount}</td><td>{charge ? `${charge.totalVoltage.toFixed(2)} ${t("common.volts")}` : "—"}</td><td>{measurement ? <div className="delta-cell"><strong>{measurement.cellDelta.toFixed(2)} {t("common.volts")}</strong><HealthBadge health={measurement.health}/></div> : "—"}</td><td><Link className="row-arrow" to={path} aria-label={t("dashboard.openBattery", { label: battery.label })}><ChevronDown size={18}/></Link></td></tr>;
      })}</tbody></table></div>}
    </section>
    {crewForm && <CrewForm groupId={selectedCrew?.groupId ?? auth.user?.groupId ?? undefined} crew={crewForm === "edit" ? selectedCrew : undefined} onClose={() => setCrewForm(null)}/>} {batteryForm && <BatteryForm crewId={activeCrewId === "all" ? undefined : activeCrewId} onClose={() => setBatteryForm(false)}/>} 
  </div>;
}
