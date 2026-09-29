import { useState, type FormEvent } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowLeft, Cog, Pencil, Plus, Replace, Trash2 } from "lucide-react";
import { Link, useParams } from "react-router-dom";
import type { DroneFlight, MotorInstallation } from "@sbm/shared";
import { api } from "../api";
import { formatFlightTime } from "../flight-time";
import { useI18n } from "../i18n";
import { Modal } from "../components/Modal";

function dateTime(value: string | null, locale: string) {
  return value ? new Date(value).toLocaleString(locale === "uk" ? "uk-UA" : "en-GB") : "—";
}

function dateTimeLocal(value: string | null) {
  if (!value) return "";
  const date = new Date(value);
  return new Date(date.getTime() - date.getTimezoneOffset() * 60_000).toISOString().slice(0, 16);
}

function HistoryTable({ history, showDrone = false }: { history: MotorInstallation[]; showDrone?: boolean }) {
  const { t, locale } = useI18n();
  return <div className="battery-table-wrap"><table className="battery-table installation-history"><thead><tr>{showDrone && <th>{t("drones.title")}</th>}<th>{t("motors.serialNumber")}</th><th>{t("equipment.position")}</th><th>{t("equipment.installedAt")}</th><th>{t("equipment.removedAt")}</th><th>{t("common.notes")}</th></tr></thead><tbody>{history.map(item => <tr key={item.id}>{showDrone && <td><Link to={`/admin/drones/${item.droneId}`}>{item.droneName}</Link></td>}<td><Link to={`/admin/motors/${item.motorId}`}>{item.serialNumber}</Link></td><td>№{item.positionNumber}</td><td>{dateTime(item.installedAt, locale)}{item.installedByUsername && <small>{item.installedByUsername}</small>}</td><td>{item.active ? <span className="equipment-status installed">{t("equipment.installed")}</span> : dateTime(item.removedAt, locale)}{item.removedByUsername && <small>{item.removedByUsername}</small>}</td><td>{item.removalNotes || item.installNotes || "—"}</td></tr>)}</tbody></table></div>;
}

function InstallMotorModal({ droneId, groupId, position, replacing, onClose }: { droneId: string; groupId: string; position: number; replacing: boolean; onClose: () => void }) {
  const { t } = useI18n(); const qc = useQueryClient();
  const motors = useQuery({ queryKey: ["motors", groupId, "stock"], queryFn: () => api.motors({ groupId, status: "stock" }) });
  const mutation = useMutation({ mutationFn: (data: { motorId: string; notes: string }) => api.installMotor(droneId, position, data), onSuccess: () => { qc.invalidateQueries({ queryKey: ["drone", droneId] }); qc.invalidateQueries({ queryKey: ["drones"] }); qc.invalidateQueries({ queryKey: ["motors"] }); onClose(); } });
  const submit = (event: FormEvent<HTMLFormElement>) => { event.preventDefault(); const data = new FormData(event.currentTarget); mutation.mutate({ motorId: String(data.get("motorId")), notes: String(data.get("notes")) }); };
  return <Modal title={replacing ? t("equipment.replaceMotor") : t("equipment.installMotor")} eyebrow={`${t("equipment.position")} №${position}`} onClose={onClose}><form className="form-grid" onSubmit={submit}><label className="full">{t("motors.serialNumber")}<select name="motorId" required defaultValue=""><option value="" disabled>{t("equipment.selectStockMotor")}</option>{motors.data?.map(motor => <option key={motor.id} value={motor.id}>{motor.serialNumber} · {formatFlightTime(motor.totalFlightSeconds, t("equipment.hoursShort"), t("common.minutesShort"))}</option>)}</select></label><label className="full">{t("common.notes")}<textarea name="notes" maxLength={1000}/></label>{motors.data?.length === 0 && <p className="form-error">{t("equipment.noStockMotors")}</p>}{mutation.error && <p className="form-error">{t("errors.generic")}</p>}<div className="form-actions full"><button type="button" className="button secondary" onClick={onClose}>{t("common.cancel")}</button><button className="button primary" disabled={mutation.isPending || !motors.data?.length}>{replacing ? t("equipment.replace") : t("equipment.install")}</button></div></form></Modal>;
}

function RemoveMotorModal({ droneId, position, onClose }: { droneId: string; position: number; onClose: () => void }) {
  const { t } = useI18n(); const qc = useQueryClient();
  const mutation = useMutation({ mutationFn: (notes: string) => api.removeMotor(droneId, position, { notes }), onSuccess: () => { qc.invalidateQueries({ queryKey: ["drone", droneId] }); qc.invalidateQueries({ queryKey: ["drones"] }); qc.invalidateQueries({ queryKey: ["motors"] }); onClose(); } });
  const submit = (event: FormEvent<HTMLFormElement>) => { event.preventDefault(); mutation.mutate(String(new FormData(event.currentTarget).get("notes"))); };
  return <Modal title={t("equipment.removeMotor")} eyebrow={`${t("equipment.position")} №${position}`} onClose={onClose}><form className="form-grid" onSubmit={submit}><p className="full modal-copy">{t("equipment.removeMotorHelp")}</p><label className="full">{t("common.notes")}<textarea name="notes" maxLength={1000}/></label>{mutation.error && <p className="form-error">{t("errors.generic")}</p>}<div className="form-actions full"><button type="button" className="button secondary" onClick={onClose}>{t("common.cancel")}</button><button className="button destructive" disabled={mutation.isPending}>{t("equipment.remove")}</button></div></form></Modal>;
}

function FlightCorrectionModal({ flight, onClose }: { flight: DroneFlight; onClose: () => void }) {
  const { t } = useI18n();
  const qc = useQueryClient();
  const mutation = useMutation({
    mutationFn: (data: { armedAt: string; disarmedAt: string | null; excluded: boolean; notes: string }) => api.correctFlight(flight.id, data),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["drone", flight.droneId] });
      qc.invalidateQueries({ queryKey: ["drones"] });
      qc.invalidateQueries({ queryKey: ["motors"] });
      onClose();
    }
  });
  const submit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const data = new FormData(event.currentTarget);
    const disarmedAt = String(data.get("disarmedAt"));
    mutation.mutate({
      armedAt: new Date(String(data.get("armedAt"))).toISOString(),
      disarmedAt: disarmedAt ? new Date(disarmedAt).toISOString() : null,
      excluded: data.get("excluded") === "on",
      notes: String(data.get("notes"))
    });
  };
  return <Modal title={t("equipment.correctFlight")} eyebrow={t("equipment.flightHistory")} onClose={onClose}>
    <form className="form-grid" onSubmit={submit}>
      <p className="full modal-copy">{t("equipment.correctFlightHelp")}</p>
      <label>{t("equipment.armedAt")}<input type="datetime-local" name="armedAt" required defaultValue={dateTimeLocal(flight.armedAt)}/></label>
      <label>{t("equipment.disarmedAt")}<input type="datetime-local" name="disarmedAt" defaultValue={dateTimeLocal(flight.disarmedAt)}/></label>
      <label className="full flight-excluded-check"><span><input type="checkbox" name="excluded" defaultChecked={flight.status === "excluded"}/>{t("equipment.excludeFlight")}</span><small>{t("equipment.excludeFlightHelp")}</small></label>
      <label className="full">{t("equipment.correctionReason")}<textarea name="notes" required maxLength={1000}/></label>
      {mutation.error && <p className="form-error">{t("errors.generic")}</p>}
      <div className="form-actions full"><button type="button" className="button secondary" onClick={onClose}>{t("common.cancel")}</button><button className="button primary" disabled={mutation.isPending}>{t("common.save")}</button></div>
    </form>
  </Modal>;
}

export function DroneDetails() {
  const { id = "" } = useParams(); const { t, locale } = useI18n();
  const query = useQuery({ queryKey: ["drone", id], queryFn: () => api.drone(id), enabled: Boolean(id) });
  const [installing, setInstalling] = useState<{ position: number; replacing: boolean } | null>(null); const [removing, setRemoving] = useState<number | null>(null); const [editingFlight, setEditingFlight] = useState<DroneFlight | null>(null);
  const drone = query.data;
  if (query.isLoading) return <div className="page"><div className="empty">{t("dashboard.loading")}</div></div>;
  if (!drone) return <div className="page"><Link className="back-link" to="/admin/drones"><ArrowLeft/>{t("drones.title")}</Link><div className="empty error">{t("errors.generic")}</div></div>;
  return <div className="page equipment-detail"><Link className="back-link" to="/admin/drones"><ArrowLeft/>{t("drones.title")}</Link><section className="detail-title"><div><span className="eyebrow">{t("equipment.configuration")}</span><h1>{drone.name}</h1><p>{drone.model} · №{drone.crewNumber} {drone.crewName}</p></div><span className={`equipment-status ${drone.status}`}>{t(`equipment.${drone.status}`)}</span></section>
    <section className="motor-slot-grid">{drone.slots.map(slot => <article className={slot.installation ? "occupied" : ""} key={slot.positionNumber}><div className="motor-slot-number"><small>{t("equipment.position")}</small><strong>№{slot.positionNumber}</strong></div>{slot.installation ? <div className="motor-slot-info"><Link to={`/admin/motors/${slot.installation.motorId}`}>{slot.installation.serialNumber}</Link><small>{t("equipment.installedAt")}: {dateTime(slot.installation.installedAt, locale)}</small></div> : <div className="motor-slot-info"><strong>{t("equipment.emptyPosition")}</strong><small>{t("equipment.chooseMotor")}</small></div>}<div className="motor-slot-actions"><button className="button compact" disabled={drone.status === "retired"} onClick={() => setInstalling({ position: slot.positionNumber, replacing: Boolean(slot.installation) })}>{slot.installation ? <Replace/> : <Plus/>}{slot.installation ? t("equipment.replace") : t("equipment.install")}</button>{slot.installation && <button className="icon-button" onClick={() => setRemoving(slot.positionNumber)} aria-label={t("equipment.remove")}><Trash2/></button>}</div></article>)}</section>
    <section className="panel flight-history-panel"><div className="panel-head"><div><h2>{t("equipment.flightHistory")}</h2><p>{t("equipment.flightHistoryHelp")}</p></div><strong>{formatFlightTime(drone.totalFlightSeconds, t("equipment.hoursShort"), t("common.minutesShort"))}</strong></div>{drone.flights.length ? <div className="battery-table-wrap"><table className="battery-table flight-history"><thead><tr><th>{t("equipment.armedAt")}</th><th>{t("equipment.disarmedAt")}</th><th>{t("equipment.duration")}</th><th>{t("motors.title")}</th><th>{t("equipment.status")}</th><th/></tr></thead><tbody>{drone.flights.map(flight => <tr key={flight.id} className={flight.status === "excluded" ? "disabled-row" : ""}><td>{dateTime(flight.armedAt, locale)}</td><td>{dateTime(flight.disarmedAt, locale)}</td><td>{flight.durationSeconds === null ? "—" : formatFlightTime(flight.durationSeconds, t("equipment.hoursShort"), t("common.minutesShort"))}</td><td><div className="flight-motors">{flight.motors.map(motor => <Link key={motor.motorId} to={`/admin/motors/${motor.motorId}`}>№{motor.positionNumber} · {motor.serialNumber}</Link>)}</div></td><td><span className={`equipment-status ${flight.status}`}>{t(`equipment.${flight.status}`)}</span>{flight.corrections.length > 0 && <details className="flight-corrections"><summary>{t("equipment.corrections")}: {flight.corrections.length}</summary>{flight.corrections.map(correction => <div key={correction.id}><strong>{dateTime(correction.correctedAt, locale)}{correction.correctedByUsername ? ` · ${correction.correctedByUsername}` : ""}</strong><span>{correction.notes}</span></div>)}</details>}</td><td><button className="icon-button" onClick={() => setEditingFlight(flight)} aria-label={t("equipment.correctFlight")}><Pencil/></button></td></tr>)}</tbody></table></div> : <div className="empty">{t("equipment.noFlights")}</div>}</section>
    <section className="panel"><div className="panel-head"><div><h2>{t("equipment.installationHistory")}</h2><p>{t("equipment.installationHistoryHelp")}</p></div></div>{drone.installationHistory.length ? <HistoryTable history={drone.installationHistory}/> : <div className="empty">{t("equipment.noInstallationHistory")}</div>}</section>
    {installing && <InstallMotorModal droneId={drone.id} groupId={drone.groupId} position={installing.position} replacing={installing.replacing} onClose={() => setInstalling(null)}/>} {removing !== null && <RemoveMotorModal droneId={drone.id} position={removing} onClose={() => setRemoving(null)}/>} {editingFlight && <FlightCorrectionModal flight={editingFlight} onClose={() => setEditingFlight(null)}/>}</div>;
}

export function MotorDetails() {
  const { id = "" } = useParams(); const { t } = useI18n(); const query = useQuery({ queryKey: ["motor", id], queryFn: () => api.motor(id), enabled: Boolean(id) }); const motor = query.data;
  if (query.isLoading) return <div className="page"><div className="empty">{t("dashboard.loading")}</div></div>;
  if (!motor) return <div className="page"><Link className="back-link" to="/admin/motors"><ArrowLeft/>{t("motors.title")}</Link><div className="empty error">{t("errors.generic")}</div></div>;
  return <div className="page equipment-detail"><Link className="back-link" to="/admin/motors"><ArrowLeft/>{t("motors.title")}</Link><section className="detail-title"><div className="battery-identity"><span className="large-battery"><Cog/></span><div><span className="eyebrow">{t("motors.eyebrow")}</span><h1>{motor.serialNumber}</h1><p>{motor.currentDroneName ? `${motor.currentDroneName} · №${motor.positionNumber}` : t(`equipment.${motor.status}`)}</p></div></div><span className={`equipment-status ${motor.status}`}>{t(`equipment.${motor.status}`)}</span></section><section className="detail-metrics"><article><small>{t("equipment.flightTime")}</small><strong>{formatFlightTime(motor.totalFlightSeconds, t("equipment.hoursShort"), t("common.minutesShort"))}</strong></article><article><small>{t("equipment.status")}</small><strong>{t(`equipment.${motor.status}`)}</strong></article></section><section className="panel"><div className="panel-head"><div><h2>{t("equipment.installationHistory")}</h2><p>{t("equipment.motorHistoryHelp")}</p></div></div>{motor.installationHistory.length ? <HistoryTable history={motor.installationHistory} showDrone/> : <div className="empty">{t("equipment.noInstallationHistory")}</div>}</section></div>;
}
