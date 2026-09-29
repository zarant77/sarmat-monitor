import { useEffect, useState, type FormEvent } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArchiveRestore, Pencil, Plus, Power, Search } from "lucide-react";
import { Link } from "react-router-dom";
import type { Drone, Motor, MotorStatus } from "@sbm/shared";
import { api } from "../api";
import { useAuth } from "../auth";
import { formatFlightTime, flightTimeToSeconds, splitFlightTime } from "../flight-time";
import { useI18n } from "../i18n";
import { Modal } from "../components/Modal";

function FlightTimeFields({ seconds = 0 }: { seconds?: number }) {
  const { t } = useI18n(); const initial = splitFlightTime(seconds);
  return <><label>{t("equipment.initialHours")}<input name="flightHours" type="number" min="0" step="1" defaultValue={initial.hours} required/></label><label>{t("equipment.initialMinutes")}<input name="flightMinutes" type="number" min="0" max="59" step="1" defaultValue={initial.minutes} required/></label></>;
}

function DroneForm({ drone, groupId, onClose }: { drone?: Drone; groupId?: string; onClose: () => void }) {
  const { t } = useI18n(); const qc = useQueryClient();
  const crews = useQuery({ queryKey: ["crews", groupId], queryFn: () => api.crews(groupId), enabled: Boolean(groupId) });
  const mutation = useMutation({ mutationFn: (data: Parameters<typeof api.createDrone>[0]) => drone ? api.updateDrone(drone.id, data) : api.createDrone(data), onSuccess: () => { qc.invalidateQueries({ queryKey: ["drones"] }); onClose(); } });
  const submit = (event: FormEvent<HTMLFormElement>) => { event.preventDefault(); const data = new FormData(event.currentTarget); mutation.mutate({ crewId: String(data.get("crewId")), name: String(data.get("name")), model: String(data.get("model")), motorCount: Number(data.get("motorCount")) === 6 ? 6 : 4, initialFlightSeconds: flightTimeToSeconds(Number(data.get("flightHours")), Number(data.get("flightMinutes"))), notes: String(data.get("notes")) }); };
  return <Modal title={drone ? t("drones.edit") : t("drones.create")} eyebrow={t("drones.eyebrow")} onClose={onClose}><form className="form-grid" onSubmit={submit}>
    <label className="full">{t("drones.crew")}<select name="crewId" defaultValue={drone?.crewId ?? ""} required><option value="" disabled>{t("drones.selectCrew")}</option>{crews.data?.map(crew => <option value={crew.id} key={crew.id}>№{crew.number} · {crew.name}</option>)}</select></label>
    <label>{t("drones.name")}<input name="name" defaultValue={drone?.name} maxLength={100} required/></label><label>{t("drones.model")}<input name="model" defaultValue={drone?.model} maxLength={120} required/></label>
    <label className="full">{t("drones.motorCount")}<select name="motorCount" defaultValue={drone?.motorCount ?? 4}><option value="4">4</option><option value="6">6</option></select></label><FlightTimeFields seconds={drone?.initialFlightSeconds}/>
    <label className="full">{t("common.notes")}<textarea name="notes" defaultValue={drone?.notes} maxLength={2000}/></label>{mutation.error && <p className="form-error">{t("errors.generic")}</p>}<div className="form-actions full"><button type="button" className="button secondary" onClick={onClose}>{t("common.cancel")}</button><button className="button primary" disabled={mutation.isPending}>{t("common.save")}</button></div>
  </form></Modal>;
}

function MotorForm({ motor, groupId, onClose }: { motor?: Motor; groupId?: string; onClose: () => void }) {
  const { t } = useI18n(); const qc = useQueryClient();
  const mutation = useMutation({ mutationFn: (data: Parameters<typeof api.createMotor>[0]) => motor ? api.updateMotor(motor.id, data) : api.createMotor(data), onSuccess: () => { qc.invalidateQueries({ queryKey: ["motors"] }); onClose(); } });
  const submit = (event: FormEvent<HTMLFormElement>) => { event.preventDefault(); const data = new FormData(event.currentTarget); mutation.mutate({ groupId, serialNumber: String(data.get("serialNumber")), type: String(data.get("type")) === "CCV" ? "CCV" : "CV", initialFlightSeconds: flightTimeToSeconds(Number(data.get("flightHours")), Number(data.get("flightMinutes"))), notes: String(data.get("notes")) }); };
  return <Modal title={motor ? t("motors.edit") : t("motors.create")} eyebrow={t("motors.eyebrow")} onClose={onClose}><form className="form-grid" onSubmit={submit}>
    <label>{t("motors.serialNumber")}<input name="serialNumber" defaultValue={motor?.serialNumber} maxLength={100} required/></label><label>{t("motors.type")}<select name="type" defaultValue={motor?.type ?? "CV"} required><option value="CV">CV</option><option value="CCV">CCV</option></select></label><FlightTimeFields seconds={motor?.initialFlightSeconds}/><label className="full">{t("common.notes")}<textarea name="notes" defaultValue={motor?.notes} maxLength={2000}/></label>{mutation.error && <p className="form-error">{t("errors.generic")}</p>}<div className="form-actions full"><button type="button" className="button secondary" onClick={onClose}>{t("common.cancel")}</button><button className="button primary" disabled={mutation.isPending}>{t("common.save")}</button></div>
  </form></Modal>;
}

function GroupPicker({ value, onChange }: { value?: string; onChange: (value: string) => void }) {
  const { t } = useI18n(); const auth = useAuth(); const groups = useQuery({ queryKey: ["groups"], queryFn: api.groups });
  useEffect(() => { if (auth.user?.role === "SUPER_ADMIN" && !value && groups.data?.[0]) onChange(groups.data[0].id); }, [auth.user?.role, groups.data, onChange, value]);
  if (auth.user?.role !== "SUPER_ADMIN") return null;
  return <select value={value ?? ""} onChange={event => onChange(event.target.value)} aria-label={t("groups.select")}><option value="" disabled>{t("groups.select")}</option>{groups.data?.map(group => <option value={group.id} key={group.id}>{group.name}</option>)}</select>;
}

function EquipmentLifecycleModal({ kind, item, onClose }: { kind: "drone" | "motor"; item: Drone | Motor; onClose: () => void }) {
  const { t } = useI18n();
  const qc = useQueryClient();
  const restoring = item.status === "retired";
  const label = kind === "drone" ? (item as Drone).name : (item as Motor).serialNumber;
  const mutation = useMutation<Drone | Motor>({
    mutationFn: () => kind === "drone"
      ? restoring ? api.restoreDrone(item.id) : api.retireDrone(item.id)
      : restoring ? api.restoreMotor(item.id) : api.retireMotor(item.id),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: [kind === "drone" ? "drones" : "motors"] });
      qc.invalidateQueries({ queryKey: [kind, item.id] });
      onClose();
    }
  });
  const action = restoring ? "restore" : "retire";
  return <Modal title={t(`equipment.${action}${kind === "drone" ? "Drone" : "Motor"}`)} eyebrow={label} onClose={onClose}>
    <div className="confirmation-content">
      <p>{t(`equipment.${action}${kind === "drone" ? "Drone" : "Motor"}Help`)}</p>
      {mutation.error && <p className="form-error">{t("errors.generic")}</p>}
      <div className="form-actions"><button type="button" className="button secondary" onClick={onClose}>{t("common.cancel")}</button><button type="button" className={`button ${restoring ? "primary" : "destructive"}`} disabled={mutation.isPending} onClick={() => mutation.mutate()}>{t(`equipment.${action}`)}</button></div>
    </div>
  </Modal>;
}

export function AdminDrones() {
  const { t, locale } = useI18n(); const auth = useAuth();
  const [groupId, setGroupId] = useState(auth.user?.groupId ?? ""); const [crewId, setCrewId] = useState(""); const [includeRetired, setIncludeRetired] = useState(false);
  const effectiveGroupId = auth.user?.role === "SUPER_ADMIN" ? groupId || undefined : auth.user?.groupId ?? undefined;
  const crews = useQuery({ queryKey: ["crews", effectiveGroupId], queryFn: () => api.crews(effectiveGroupId), enabled: Boolean(effectiveGroupId) });
  const query = useQuery({ queryKey: ["drones", effectiveGroupId, crewId, includeRetired], queryFn: () => api.drones({ groupId: effectiveGroupId, crewId: crewId || undefined, includeRetired }), enabled: Boolean(effectiveGroupId) });
  const [editing, setEditing] = useState<Drone | "new" | null>(null);
  const [lifecycleTarget, setLifecycleTarget] = useState<Drone | null>(null);
  const hours = (seconds: number) => formatFlightTime(seconds, t("equipment.hoursShort"), t("common.minutesShort"));
  return <div className="page"><section className="hero-row"><div><span className="eyebrow">{t("drones.eyebrow")}</span><h1>{t("drones.title")}</h1><p>{t("drones.description")}</p></div><div className="hero-actions"><GroupPicker value={effectiveGroupId} onChange={value => { setGroupId(value); setCrewId(""); }}/><button className="button primary" disabled={!effectiveGroupId} onClick={() => setEditing("new")}><Plus/>{t("drones.new")}</button></div></section>
    <section className="panel"><div className="panel-head"><div><h2>{t("drones.registry")}</h2><p>{t("drones.registryHelp")}</p></div><div className="table-tools"><select value={crewId} onChange={event => setCrewId(event.target.value)}><option value="">{t("drones.allCrews")}</option>{crews.data?.map(crew => <option value={crew.id} key={crew.id}>№{crew.number} · {crew.name}</option>)}</select><label className="inline-check"><input type="checkbox" checked={includeRetired} onChange={event => setIncludeRetired(event.target.checked)}/>{t("equipment.showRetired")}</label></div></div>
      <div className="battery-table-wrap"><table className="battery-table equipment-table"><thead><tr><th>{t("drones.name")}</th><th>{t("drones.model")}</th><th>{t("drones.crew")}</th><th>{t("drones.motorCount")}</th><th>{t("equipment.flightTime")}</th><th>{t("equipment.status")}</th><th/></tr></thead><tbody>{query.data?.map(drone => <tr key={drone.id} className={drone.status === "retired" ? "disabled-row" : drone.openFlightStartedAt ? "open-flight-row" : ""}><td><Link to={`/admin/drones/${drone.id}`}><strong>{drone.name}</strong></Link>{drone.openFlightStartedAt && <small className="open-flight-warning">{t("equipment.openFlightSince", { time: new Date(drone.openFlightStartedAt).toLocaleString(locale === "uk" ? "uk-UA" : "en-GB") })}</small>}</td><td>{drone.model}</td><td>№{drone.crewNumber} · {drone.crewName}</td><td>{drone.installedMotorCount}/{drone.motorCount}</td><td>{hours(drone.totalFlightSeconds)}</td><td><span className={`equipment-status ${drone.openFlightStartedAt ? "armed" : drone.status}`}>{t(`equipment.${drone.openFlightStartedAt ? "armed" : drone.status}`)}</span></td><td><div className="crew-actions"><button className="icon-button" onClick={() => setEditing(drone)} aria-label={t("common.edit")}><Pencil/></button><button className="button compact lifecycle-button" disabled={Boolean(drone.openFlightStartedAt)} onClick={() => setLifecycleTarget(drone)}>{drone.status === "retired" ? <ArchiveRestore/> : <Power/>}{drone.status === "retired" ? t("equipment.restore") : t("equipment.retire")}</button></div></td></tr>)}</tbody></table></div>{query.isLoading && <div className="empty">{t("dashboard.loading")}</div>}{!query.isLoading && !query.data?.length && <div className="empty">{t("drones.empty")}</div>}{query.error && <p className="admin-error">{t("errors.generic")}</p>}
    </section>{editing && <DroneForm drone={editing === "new" ? undefined : editing} groupId={effectiveGroupId} onClose={() => setEditing(null)}/>} {lifecycleTarget && <EquipmentLifecycleModal kind="drone" item={lifecycleTarget} onClose={() => setLifecycleTarget(null)}/>}</div>;
}

export function AdminMotors() {
  const { t } = useI18n(); const auth = useAuth();
  const [groupId, setGroupId] = useState(auth.user?.groupId ?? ""); const [status, setStatus] = useState<MotorStatus | "">(""); const [search, setSearch] = useState("");
  const effectiveGroupId = auth.user?.role === "SUPER_ADMIN" ? groupId || undefined : auth.user?.groupId ?? undefined;
  const query = useQuery({ queryKey: ["motors", effectiveGroupId, status, search], queryFn: () => api.motors({ groupId: effectiveGroupId, status: status || undefined, search: search || undefined }), enabled: Boolean(effectiveGroupId) });
  const [editing, setEditing] = useState<Motor | "new" | null>(null);
  const [lifecycleTarget, setLifecycleTarget] = useState<Motor | null>(null);
  const hours = (seconds: number) => formatFlightTime(seconds, t("equipment.hoursShort"), t("common.minutesShort"));
  return <div className="page"><section className="hero-row"><div><span className="eyebrow">{t("motors.eyebrow")}</span><h1>{t("motors.title")}</h1><p>{t("motors.description")}</p></div><div className="hero-actions"><GroupPicker value={effectiveGroupId} onChange={setGroupId}/><button className="button primary" disabled={!effectiveGroupId} onClick={() => setEditing("new")}><Plus/>{t("motors.new")}</button></div></section>
    <section className="panel"><div className="panel-head"><div><h2>{t("motors.registry")}</h2><p>{t("motors.registryHelp")}</p></div><div className="table-tools"><label className="search"><Search/><input value={search} onChange={event => setSearch(event.target.value)} placeholder={t("motors.search")}/></label><select value={status} onChange={event => setStatus(event.target.value as MotorStatus | "")}><option value="">{t("equipment.allStatuses")}</option><option value="stock">{t("equipment.stock")}</option><option value="installed">{t("equipment.installed")}</option><option value="retired">{t("equipment.retired")}</option></select></div></div>
      <div className="battery-table-wrap"><table className="battery-table equipment-table"><thead><tr><th>{t("motors.serialNumber")}</th><th>{t("motors.type")}</th>{auth.user?.role === "SUPER_ADMIN" && <th>{t("nav.groups")}</th>}<th>{t("equipment.flightTime")}</th><th>{t("equipment.status")}</th><th/></tr></thead><tbody>{query.data?.map(motor => <tr key={motor.id} className={motor.status === "retired" ? "disabled-row" : ""}><td><Link to={`/admin/motors/${motor.id}`}><strong>{motor.serialNumber}</strong></Link>{motor.currentDroneName && <small>{motor.currentDroneName} · №{motor.positionNumber}</small>}{!motor.currentDroneName && motor.notes && <small>{motor.notes}</small>}</td><td><strong>{motor.type}</strong></td>{auth.user?.role === "SUPER_ADMIN" && <td>{motor.groupName}</td>}<td>{hours(motor.totalFlightSeconds)}</td><td><span className={`equipment-status ${motor.status}`}>{t(`equipment.${motor.status}`)}</span></td><td><div className="crew-actions"><button className="icon-button" onClick={() => setEditing(motor)} aria-label={t("common.edit")}><Pencil/></button><button className="button compact lifecycle-button" onClick={() => setLifecycleTarget(motor)}>{motor.status === "retired" ? <ArchiveRestore/> : <Power/>}{motor.status === "retired" ? t("equipment.restore") : t("equipment.retire")}</button></div></td></tr>)}</tbody></table></div>{query.isLoading && <div className="empty">{t("dashboard.loading")}</div>}{!query.isLoading && !query.data?.length && <div className="empty">{t("motors.empty")}</div>}{query.error && <p className="admin-error">{t("errors.generic")}</p>}
    </section>{editing && <MotorForm motor={editing === "new" ? undefined : editing} groupId={effectiveGroupId} onClose={() => setEditing(null)}/>} {lifecycleTarget && <EquipmentLifecycleModal kind="motor" item={lifecycleTarget} onClose={() => setLifecycleTarget(null)}/>}</div>;
}
