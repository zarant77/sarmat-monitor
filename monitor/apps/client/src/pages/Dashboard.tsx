import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { AlertTriangle, BatteryMedium, ChevronDown, Gauge, Pencil, Plus, Users } from "lucide-react";
import { Link, useParams } from "react-router-dom";
import { api } from "../api";
import { useAuth } from "../auth";
import { HealthBadge } from "../components/HealthBadge";
import { BatteryForm, CrewForm } from "../components/Forms";
import { useI18n } from "../i18n";
import { clientSettings } from "../client-settings";

export function Dashboard({ adminMode = false }: { adminMode?: boolean }) {
  const auth = useAuth(); const { t, locale } = useI18n(); const { crewId: routeCrewId } = useParams();
  const crews = useQuery({ queryKey: ["crews", auth.user?.groupId], queryFn: () => api.crews(), enabled: adminMode });
  const [crewId, setCrewId] = useState(() => clientSettings.getCrewId());
  const [crewForm, setCrewForm] = useState<"create" | "edit" | null>(null); const [batteryForm, setBatteryForm] = useState(false);
  const activeCrewId = routeCrewId ?? (adminMode ? crewId : undefined);
  const batteries = useQuery({ queryKey: ["batteries", activeCrewId ?? "mine", adminMode], queryFn: () => api.batteries(activeCrewId === "all" ? undefined : activeCrewId, adminMode) });
  const selectedCrew = crews.data?.find(crew => crew.id === activeCrewId);
  const changeCrew = (id: string) => { setCrewId(id); clientSettings.setCrewId(id); };
  const items = batteries.data ?? [];
  const critical = items.filter(battery => battery.latestMeasurement?.health === "danger").length;
  const attention = items.filter(battery => battery.latestMeasurement && battery.latestMeasurement.health !== "good").length;
  const totalCapacity = items.reduce((sum, battery) => sum + battery.capacityAh, 0);
  const formattedCapacity = totalCapacity.toLocaleString(locale === "uk" ? "uk-UA" : "en-US");

  if (!adminMode) return <div className="page crew-batteries-page">
    <header className="crew-page-title"><span><i className="crew-color-dot" style={{ backgroundColor: auth.user?.crewColor ?? undefined }}/>{auth.user?.crewNumber != null && `№${auth.user.crewNumber} · `}{auth.user?.crewName}</span><h1>{t("nav.batteries")}</h1></header>
    <section className="crew-summary" aria-label={t("dashboard.crewSummary.label")}>
      <div><small>{t("dashboard.crewSummary.count")}</small><strong>{items.length}</strong></div>
      <div><small>{t("dashboard.crewSummary.capacity")}</small><strong>{formattedCapacity}<em>{t("common.ampHours")}</em></strong></div>
      <div className={attention ? "attention" : ""}><small>{t("dashboard.crewSummary.attention")}</small><strong>{attention}</strong></div>
    </section>
    {batteries.isLoading ? <div className="empty">{t("dashboard.loading")}</div> : batteries.isError ? <div className="empty error">{t("dashboard.serverError")}</div> : !items.length ? <div className="empty crew-empty">{t("dashboard.empty")}</div> : <section className="crew-battery-list">{items.map(battery => {
      const measurement = battery.latestMeasurement;
      return <Link to={`/batteries/${battery.id}`} className={`crew-battery-card ${measurement?.health ?? "unchecked"}`} key={battery.id}>
        <span className="battery-avatar"><BatteryMedium/></span>
        <div className="crew-battery-main"><strong>{battery.label}</strong><small>{battery.serialNumber}</small></div>
        <span className="crew-battery-state"><i className={`state-dot ${battery.state}`}/>{t(`states.${battery.state}`)}</span>
        <div className="crew-battery-facts">
          <span><small>{t("dashboard.columns.charge")}</small><strong>{measurement?.chargePercent != null ? `${measurement.chargePercent}%` : "—"}</strong></span>
          <span><small>{t("dashboard.columns.capacity")}</small><strong>{battery.capacityAh} {t("common.ampHours")}</strong></span>
          <span><small>{t("dashboard.columns.voltage")}</small><strong>{measurement ? `${measurement.totalVoltage.toFixed(2)} ${t("common.volts")}` : "—"}</strong></span>
          <span><small>{t("dashboard.columns.delta")}</small><strong>{measurement ? `${measurement.cellDelta.toFixed(2)} ${t("common.volts")}` : "—"}</strong></span>
        </div>
        {measurement && <HealthBadge health={measurement.health}/>}<ChevronDown className="crew-card-arrow"/>
      </Link>;
    })}</section>}
  </div>;

  return <div className="page dashboard-page">
    <section className="hero-row"><div><span className="eyebrow">{t("dashboard.eyebrow")}</span><h1>{t("dashboard.title")}</h1><p>{t("dashboard.adminDescription")}</p></div><div className="hero-actions">{auth.user?.role === "GROUP_ADMIN"&&<button className="button secondary" onClick={() => setCrewForm("create")}><Users size={17}/> {t("dashboard.newCrew")}</button>}<button className="button primary" onClick={() => setBatteryForm(true)} disabled={!activeCrewId || activeCrewId === "all"}><Plus size={17}/> {t("dashboard.addBattery")}</button></div></section>
    {!routeCrewId && <section className="crew-strip" aria-label={t("dashboard.crewSwitcher")}><button className={`crew-card ${crewId === "all" ? "selected" : ""}`} onClick={() => changeCrew("all")}><span className="crew-symbol">{t("dashboard.all")}</span><span><strong>{t("dashboard.allCrews")}</strong><small>{t("dashboard.packs", { count: crews.data?.reduce((sum, crew) => sum + crew.batteryCount, 0) ?? 0 })}</small></span></button>{crews.data?.map(crew => <button key={crew.id} className={`crew-card ${crewId === crew.id ? "selected" : ""}`} onClick={() => changeCrew(crew.id)}><span className="crew-symbol color" style={{ backgroundColor: crew.color }}>{crew.number}</span><span><strong>{crew.name}</strong><small>{t("dashboard.packs", { count: crew.batteryCount })}</small></span></button>)}</section>}
    <section className="metrics-grid"><article><span className="metric-icon"><BatteryMedium/></span><div><small>{t("dashboard.packsInView")}</small><strong>{items.length}</strong><p>{t("dashboard.readyForMission", { count: items.filter(battery => battery.state === "ready").length })}</p></div></article><article><span className="metric-icon"><Gauge/></span><div><small>{t("dashboard.fleetCapacity")}</small><strong>{formattedCapacity} <em>{t("common.ampHours")}</em></strong><p>{t("dashboard.assignedCapacity")}</p></div></article><article className={critical ? "danger-metric" : ""}><span className="metric-icon"><AlertTriangle/></span><div><small>{t("dashboard.requiresAction")}</small><strong>{critical}</strong><p>{critical ? t("dashboard.dangerousImbalance") : t("dashboard.noCritical")}</p></div></article></section>
    <section className="panel fleet-panel"><div className="panel-head"><div><h2>{selectedCrew ? `№${selectedCrew.number} · ${selectedCrew.name}` : t("dashboard.batteryFleet")}</h2><p>{selectedCrew ? t("dashboard.packs", { count: selectedCrew.batteryCount }) : t("dashboard.allAssigned")}</p></div>{selectedCrew && <button className="icon-button" aria-label={t("dashboard.editCrew")} onClick={() => setCrewForm("edit")}><Pencil size={17}/></button>}</div>
      {batteries.isLoading ? <div className="empty">{t("dashboard.loading")}</div> : batteries.isError ? <div className="empty error">{t("dashboard.serverError")}</div> : !items.length ? <div className="empty">{t("dashboard.empty")}</div> : <div className="battery-table-wrap"><table className="battery-table"><thead><tr><th>{t("dashboard.columns.battery")}</th><th>{t("dashboard.columns.state")}</th><th>{t("dashboard.columns.charge")}</th><th>{t("dashboard.columns.capacity")}</th><th>{t("dashboard.columns.cycles")}</th><th>{t("dashboard.columns.voltage")}</th><th>{t("dashboard.columns.delta")}</th><th aria-label={t("common.open")}/></tr></thead><tbody>{items.map(battery => { const measurement = battery.latestMeasurement; const path = `/admin/batteries/${battery.id}`; return <tr key={battery.id} className={measurement?.health === "danger" ? "danger-row" : ""}><td><Link to={path}><span className="battery-avatar"><BatteryMedium/></span><span><strong>{battery.label}</strong><small>{battery.serialNumber} · <i className="crew-color-dot" style={{ backgroundColor: battery.crewColor }}/>№{battery.crewNumber} · {battery.crewName}</small></span></Link></td><td><span className={`state-dot ${battery.state}`}/>{t(`states.${battery.state}`)}</td><td>{measurement?.chargePercent != null ? `${measurement.chargePercent}%` : "—"}</td><td data-label={`${t("dashboard.columns.capacity")} · `}><strong>{battery.capacityAh}</strong> {t("common.ampHours")}</td><td data-label={`${t("dashboard.columns.cycles")} · `}>{battery.cycleCount}</td><td>{measurement ? `${measurement.totalVoltage.toFixed(2)} ${t("common.volts")}` : "—"}</td><td>{measurement ? <div className="delta-cell"><strong>{measurement.cellDelta.toFixed(2)} {t("common.volts")}</strong><HealthBadge health={measurement.health}/></div> : "—"}</td><td><Link className="row-arrow" to={path} aria-label={t("dashboard.openBattery", { label: battery.label })}><ChevronDown size={18}/></Link></td></tr>; })}</tbody></table></div>}
    </section>
    {crewForm && <CrewForm groupId={selectedCrew?.groupId ?? auth.user?.groupId ?? undefined} crew={crewForm === "edit" ? selectedCrew : undefined} onClose={() => setCrewForm(null)}/>} {batteryForm && <BatteryForm crewId={activeCrewId === "all" ? undefined : activeCrewId} onClose={() => setBatteryForm(false)}/>} 
  </div>;
}
