import { useState, type CSSProperties, type FormEvent } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { AlertTriangle, BatteryMedium, ChevronRight, Eye, EyeOff, KeyRound, Layers3, MoreHorizontal, Pencil, Plus, Power, RadioTower, Save, Shield, ShieldCheck, Trash2, Users, X } from "lucide-react";
import { Link, useParams } from "react-router-dom";
import type { BatteryType, Crew, Group } from "@sbm/shared";
import { api } from "../api";
import { useAuth } from "../auth";
import { CrewForm } from "../components/Forms";
import { Modal } from "../components/Modal";
import { CrewIdentity } from "../components/CrewIdentity";
import { useI18n } from "../i18n";

export function AdminDashboard() {
  const { t } = useI18n(); const auth = useAuth();
  const groups = useQuery({ queryKey: ["groups"], queryFn: api.groups });
  const crews = useQuery({ queryKey: ["crews", auth.user?.groupId], queryFn: () => api.crews() });
  const batteries = useQuery({ queryKey: ["batteries", "admin-summary"], queryFn: () => api.batteries() });
  const telemetry = useQuery({ queryKey: ["telemetry", auth.user?.groupId], queryFn: () => api.telemetry(auth.user?.groupId ?? undefined), enabled: Boolean(auth.user?.groupId), refetchInterval: 5000 });
  const danger = batteries.data?.filter(b => b.latestMeasurement?.health === "danger").length ?? 0;
  return <div className="page">
    <section className="hero-row"><div><span className="eyebrow">{t("admin.eyebrow")}</span><h1>{auth.user?.role === "GROUP_ADMIN" ? auth.user.groupName : t("admin.dashboard.title")}</h1><p>{auth.user?.role === "GROUP_ADMIN" ? t("admin.dashboard.groupDescription") : t("admin.dashboard.description")}</p></div></section>
    <section className="metrics-grid admin-metrics">
      <article><span className="metric-icon"><Shield/></span><div><small>{t("admin.dashboard.groups")}</small><strong>{groups.data?.length ?? "—"}</strong><p>{t("admin.dashboard.groupsHelp")}</p></div></article>
      <article><span className="metric-icon"><Users/></span><div><small>{t("admin.dashboard.activeCrews")}</small><strong>{crews.data?.filter(c => c.enabled).length ?? "—"}</strong><p>{t("admin.dashboard.totalCrews", { count: crews.data?.length ?? 0 })}</p></div></article>
      <article><span className="metric-icon"><BatteryMedium/></span><div><small>{t("admin.dashboard.activePacks")}</small><strong>{batteries.data?.length ?? "—"}</strong><p>{t("admin.dashboard.allAssignments")}</p></div></article>
      <article className={danger ? "danger-metric" : ""}><span className="metric-icon"><AlertTriangle/></span><div><small>{t("admin.dashboard.criticalHealth")}</small><strong>{danger}</strong><p>{t("admin.dashboard.requiresAction")}</p></div></article>
    </section>
    <section className="operational-overview" aria-label={t("admin.dashboard.operationalOverview")}>
      <div className="section-heading"><div><span className="eyebrow">{t("admin.dashboard.liveStatus")}</span><h2>{t("admin.dashboard.crewReadiness")}</h2></div><span className="telemetry-connection"><i/>{t("telemetry.updating")}</span></div>
      <div className="operational-crew-grid">{crews.data?.map(crew => {
        const crewBatteries = batteries.data?.filter(battery => battery.crewId === crew.id) ?? [];
        const ready = crewBatteries.filter(battery => battery.state === "ready").length;
        const attention = crewBatteries.filter(battery => battery.latestMeasurement && battery.latestMeasurement.health !== "good").length;
        const live = telemetry.data?.crews.find(item => item.id === crew.id); const online = Boolean(live?.snapshot && live.snapshot[0] === 0);
        return <Link to={`/admin/crews/${crew.id}`} className={`operational-crew-card ${attention ? "needs-attention" : ""}`} style={{ "--crew-color": crew.color } as CSSProperties} key={crew.id}>
          <div className="operational-crew-head"><CrewIdentity number={crew.number} name={crew.name} color={crew.color}/><strong className={crew.enabled ? "ready" : "offline"}>{crew.enabled ? t("admin.dashboard.ready") : t("common.disabled")}</strong></div>
          <p>{t("admin.dashboard.batteryReadiness", { total: crewBatteries.length, ready })}{attention ? <b> · {t("admin.dashboard.attentionCount", { count: attention })}</b> : null}</p>
          <div className={`operational-link ${online ? "online" : "offline"}`}><RadioTower/>{t("admin.dashboard.telemetryStatus")}: <strong>{online ? t("admin.dashboard.online") : t("admin.dashboard.offline")}</strong><i/></div>
        </Link>;
      })}</div>
    </section>
    <section className="admin-shortcuts">
      {auth.user?.role === "SUPER_ADMIN"&&<Link to="/admin/groups"><Shield/><span><strong>{t("admin.dashboard.manageGroups")}</strong><small>{t("admin.dashboard.manageGroupsHelp")}</small></span><ChevronRight/></Link>}
      <Link to="/admin/crews"><Users/><span><strong>{t("admin.dashboard.manageCrews")}</strong><small>{t("admin.dashboard.manageCrewsHelp")}</small></span><ChevronRight/></Link>
      <Link to="/admin/batteries"><BatteryMedium/><span><strong>{t("admin.dashboard.manageBatteries")}</strong><small>{t("admin.dashboard.manageBatteriesHelp")}</small></span><ChevronRight/></Link>
      {auth.user?.role === "SUPER_ADMIN"&&<><Link to="/admin/battery-types"><Layers3/><span><strong>{t("admin.dashboard.manageBatteryTypes")}</strong><small>{t("admin.dashboard.manageBatteryTypesHelp")}</small></span><ChevronRight/></Link>
      <Link to="/admin/settings"><ShieldCheck/><span><strong>{t("admin.dashboard.globalSettings")}</strong><small>{t("admin.dashboard.globalSettingsHelp")}</small></span><ChevronRight/></Link></>}
      <Link to="/admin/telemetry"><RadioTower/><span><strong>{t("admin.dashboard.telemetry")}</strong><small>{t("admin.dashboard.telemetryHelp")}</small></span><ChevronRight/></Link>
    </section>
  </div>;
}

function GroupForm({ group, onClose }: { group?: Group; onClose: () => void }) {
  const { t } = useI18n(); const qc = useQueryClient();
  const mutation = useMutation({ mutationFn: (data: any) => group ? api.updateGroup(group.id, data) : api.createGroup(data), onSuccess: () => { qc.invalidateQueries({ queryKey: ["groups"] }); onClose(); } });
  const submit = (event: FormEvent<HTMLFormElement>) => { event.preventDefault(); const data = new FormData(event.currentTarget); mutation.mutate({ name: data.get("name"), code: data.get("code"), notes: data.get("notes"), enabled: group?.enabled ?? true }); };
  return <Modal title={group ? t("groups.edit") : t("groups.create")} eyebrow={t("groups.eyebrow")} onClose={onClose}><form className="form-grid" onSubmit={submit}>
    <label>{t("groups.name")}<input name="name" defaultValue={group?.name} required/></label><label>{t("groups.code")}<input name="code" defaultValue={group?.code}/></label>
    <label className="full">{t("common.notes")}<textarea name="notes" defaultValue={group?.notes}/></label>{mutation.error&&<p className="form-error">{t("errors.generic")}</p>}<div className="form-actions full"><button type="button" className="button secondary" onClick={onClose}>{t("common.cancel")}</button><button className="button primary">{t("common.save")}</button></div>
  </form></Modal>;
}

function GroupAdministrators({ group }: { group: Group }) {
  const { t } = useI18n(); const qc = useQueryClient(); const [showPassword, setShowPassword] = useState(false);
  const users = useQuery({ queryKey: ["users", "group", group.id], queryFn: () => api.users(undefined, group.id) });
  const admins = users.data?.filter(user => user.role === "GROUP_ADMIN") ?? [];
  const create = useMutation({ mutationFn: (data: any) => api.createGroupAdmin({ ...data, groupId: group.id }), onSuccess: () => qc.invalidateQueries({ queryKey: ["users", "group", group.id] }) });
  const update = useMutation({ mutationFn: ({ id, enabled }: { id: string; enabled: boolean }) => api.updateCredential(id, { enabled }), onSuccess: () => qc.invalidateQueries({ queryKey: ["users", "group", group.id] }) });
  const remove = useMutation({ mutationFn: api.deleteCredential, onSuccess: () => qc.invalidateQueries({ queryKey: ["users", "group", group.id] }) });
  return <section className="panel group-admin-panel"><div className="panel-head"><div><h2>{t("groups.administrators")}</h2><p>{t("groups.administratorsHelp")}</p></div></div><div className="credential-list">{admins.map(user=><article key={user.id}><span className={`access-status ${user.enabled?"active":"disabled"}`}><ShieldCheck/></span><div><strong>{user.username}</strong><small>{user.enabled?t("admin.credentials.canSignIn"):t("admin.credentials.accessDisabled")}</small></div><button className="icon-button" onClick={()=>update.mutate({id:user.id,enabled:!user.enabled})} aria-label={t("common.disable")}><Power/></button><button className="icon-button destructive" onClick={()=>remove.mutate(user.id)} aria-label={t("common.delete")}><Trash2/></button></article>)}</div><form className="credential-create" autoComplete="off" onSubmit={event=>{event.preventDefault();const form=event.currentTarget;const data=new FormData(form);create.mutate({username:String(data.get("username")),password:String(data.get("password")),enabled:true},{onSuccess:()=>form.reset()});}}><h3>{t("groups.addAdministrator")}</h3><div><label>{t("admin.credentials.username")}<input name="username" autoComplete="off" minLength={3} required/></label><label>{t("admin.credentials.temporaryPassword")}<span className="password-field"><input name="password" autoComplete="new-password" type={showPassword?"text":"password"} minLength={10} required/><button type="button" className="password-toggle" onClick={()=>setShowPassword(value=>!value)}>{showPassword?<EyeOff/>:<Eye/>}</button></span></label><button className="button primary"><Plus/>{t("admin.credentials.create")}</button></div></form></section>;
}

export function AdminGroups() {
  const { t } = useI18n(); const qc = useQueryClient(); const groups = useQuery({ queryKey: ["groups"], queryFn: api.groups }); const [editing,setEditing]=useState<Group|"new"|null>(null);
  const status=useMutation({mutationFn:({group,enabled}:{group:Group;enabled:boolean})=>api.updateGroup(group.id,{enabled}),onSuccess:()=>qc.invalidateQueries({queryKey:["groups"]})});
  return <div className="page"><section className="hero-row"><div><span className="eyebrow">{t("groups.eyebrow")}</span><h1>{t("groups.title")}</h1><p>{t("groups.description")}</p></div><button className="button primary" onClick={()=>setEditing("new")}><Plus/>{t("groups.new")}</button></section><section className="group-grid">{groups.data?.map(group=><article className={`panel group-card ${!group.enabled?"disabled-row":""}`} key={group.id}><div className="group-card-head"><span className="type-icon"><Shield/></span><div><strong>{group.name}</strong><small>{group.code||t("groups.noCode")}</small></div></div><div className="group-stats"><span><strong>{group.crewCount}</strong><small>{t("nav.crews")}</small></span><span><strong>{group.batteryCount}</strong><small>{t("nav.batteries")}</small></span><span className={group.warningCount?"warning":""}><strong>{group.warningCount}</strong><small>{t("groups.warnings")}</small></span></div><div className="crew-actions"><Link className="button compact" to={`/admin/groups/${group.id}`}>{t("common.open")}<ChevronRight/></Link><button className="icon-button" onClick={()=>setEditing(group)}><Pencil/></button><button className="icon-button" onClick={()=>status.mutate({group,enabled:!group.enabled})}><Power/></button></div></article>)}</section>{editing&&<GroupForm group={editing==="new"?undefined:editing} onClose={()=>setEditing(null)}/>}</div>;
}

export function AdminGroupDetails() {
  const { groupId="" }=useParams(); const {t}=useI18n(); const group=useQuery({queryKey:["group",groupId],queryFn:()=>api.group(groupId)}); const crews=useQuery({queryKey:["crews",groupId],queryFn:()=>api.crews(groupId)}); const batteries=useQuery({queryKey:["batteries","group",groupId],queryFn:()=>api.batteries(undefined,true,groupId)});
  if(!group.data) return <div className="page"><div className="empty">{t("dashboard.loading")}</div></div>;
  return <div className="page"><section className="hero-row"><div><span className="eyebrow">{group.data.code}</span><h1>{group.data.name}</h1><p>{group.data.notes}</p></div></section><section className="metrics-grid"><article><span className="metric-icon"><Users/></span><div><small>{t("nav.crews")}</small><strong>{crews.data?.length??0}</strong></div></article><article><span className="metric-icon"><BatteryMedium/></span><div><small>{t("nav.batteries")}</small><strong>{batteries.data?.length??0}</strong></div></article><article><span className="metric-icon"><AlertTriangle/></span><div><small>{t("groups.warnings")}</small><strong>{batteries.data?.filter(b=>b.latestMeasurement?.health!=="good").length??0}</strong></div></article></section><section className="panel"><div className="panel-head"><div><h2>{t("nav.crews")}</h2><p>{t("groups.crewsHelp")}</p></div></div><div className="admin-crew-list">{crews.data?.map(crew=><article key={crew.id}><div className="crew-list-main"><CrewIdentity number={crew.number} name={crew.name} color={crew.color} size="large" suffix={crew.reserve ? <small className="crew-identity-suffix">· {t("groups.reserve")}</small> : undefined}/><small>{t("admin.crews.summary",{batteries:crew.batteryCount,credentials:crew.userCount??0})}</small></div><Link className="icon-button" to={`/admin/crews/${crew.id}`}><BatteryMedium/></Link></article>)}</div></section><GroupAdministrators group={group.data}/></div>;
}

function Credentials({ crew, onClose }: { crew: Crew; onClose: () => void }) {
  const { t } = useI18n(); const qc = useQueryClient();
  const [showCreatePassword, setShowCreatePassword] = useState(false); const [resetUserId, setResetUserId] = useState<string | null>(null); const [resetPassword, setResetPassword] = useState(""); const [showResetPassword, setShowResetPassword] = useState(false);
  const users = useQuery({ queryKey: ["users", crew.id], queryFn: () => api.users(crew.id) });
  const create = useMutation({ mutationFn: (data: { username: string; password: string; enabled: boolean }) => api.createCredential(crew.id, data), onSuccess: () => qc.invalidateQueries({ queryKey: ["users", crew.id] }) });
  const update = useMutation({ mutationFn: ({ id, data }: { id: string; data: any }) => api.updateCredential(id, data), onSuccess: () => qc.invalidateQueries({ queryKey: ["users", crew.id] }) });
  const remove = useMutation({ mutationFn: api.deleteCredential, onSuccess: () => qc.invalidateQueries({ queryKey: ["users", crew.id] }) });
  const submit = (event: FormEvent<HTMLFormElement>) => { event.preventDefault(); const form = event.currentTarget; const data = new FormData(form); create.mutate({ username: String(data.get("username")), password: String(data.get("password")), enabled: true }, { onSuccess: () => form.reset() }); };
  return <Modal title={t("admin.credentials.title", { name: `№${crew.number} · ${crew.name}` })} eyebrow={t("admin.credentials.eyebrow")} onClose={onClose}>
    <div className="credential-list">{users.data?.map(user => <article key={user.id}>
      <span className={`access-status ${user.enabled ? "active" : "disabled"}`}><KeyRound/></span><div><strong>{user.username}</strong><small>{user.enabled ? t("admin.credentials.canSignIn") : t("admin.credentials.accessDisabled")}</small></div>
      <button className="button compact" onClick={() => { setResetUserId(user.id); setResetPassword(""); setShowResetPassword(false); }}><KeyRound/> {t("admin.credentials.reset")}</button>
      <button className="button compact" onClick={() => update.mutate({ id: user.id, data: { enabled: !user.enabled } })}><Power/>{user.enabled ? t("common.disable") : t("common.enable")}</button>
      <button className="icon-button destructive" onClick={() => confirm(t("admin.credentials.deleteConfirm", { username: user.username })) && remove.mutate(user.id)} aria-label={t("admin.credentials.delete")}><Trash2/></button>
      {resetUserId === user.id && <form className="credential-reset" onSubmit={event => { event.preventDefault(); update.mutate({ id: user.id, data: { password: resetPassword } }, { onSuccess: () => { setResetUserId(null); setResetPassword(""); } }); }}><label>{t("admin.credentials.newPassword")}<span className="password-field"><input value={resetPassword} onChange={event => setResetPassword(event.target.value)} type={showResetPassword ? "text" : "password"} minLength={10} required autoFocus/><button type="button" className="password-toggle" onClick={() => setShowResetPassword(value => !value)} aria-label={showResetPassword ? t("common.hidePassword") : t("common.showPassword")}>{showResetPassword ? <EyeOff/> : <Eye/>}</button></span></label><button className="icon-button" aria-label={t("common.save")}><Save/></button><button type="button" className="icon-button" onClick={() => setResetUserId(null)} aria-label={t("common.cancel")}><X/></button></form>}
    </article>)}</div>
    <form className="credential-create" autoComplete="off" onSubmit={submit}><h3>{t("admin.credentials.issue")}</h3><div><label>{t("admin.credentials.username")}<input name="username" autoComplete="off" required minLength={3}/></label><label>{t("admin.credentials.temporaryPassword")}<span className="password-field"><input name="password" autoComplete="new-password" type={showCreatePassword ? "text" : "password"} required minLength={10}/><button type="button" className="password-toggle" onClick={() => setShowCreatePassword(value => !value)} aria-label={showCreatePassword ? t("common.hidePassword") : t("common.showPassword")}>{showCreatePassword ? <EyeOff/> : <Eye/>}</button></span></label><button className="button primary"><Plus/> {t("admin.credentials.create")}</button></div>{(create.error || update.error || remove.error) && <p className="form-error">{t("errors.generic")}</p>}</form>
  </Modal>;
}

export function AdminCrews() {
  const { t } = useI18n(); const qc = useQueryClient(); const auth=useAuth();
  const groups=useQuery({queryKey:["groups"],queryFn:api.groups}); const [selectedGroupId,setSelectedGroupId]=useState(auth.user?.groupId??"");
  const effectiveGroupId=auth.user?.role==="SUPER_ADMIN"?(selectedGroupId||groups.data?.[0]?.id):auth.user?.groupId??undefined;
  const query = useQuery({ queryKey: ["crews",effectiveGroupId], queryFn: () => api.crews(effectiveGroupId), enabled:Boolean(effectiveGroupId) });
  const [edit, setEdit] = useState<Crew | "new" | null>(null); const [credentials, setCredentials] = useState<Crew | null>(null);
  const status = useMutation({ mutationFn: ({ crew, enabled }: { crew: Crew; enabled: boolean }) => api.updateCrew(crew.id, { enabled }), onSuccess: () => qc.invalidateQueries({ queryKey: ["crews"] }) });
  const remove = useMutation({ mutationFn: api.deleteCrew, onSuccess: () => qc.invalidateQueries({ queryKey: ["crews"] }) });
  return <div className="page">
    <section className="hero-row"><div><span className="eyebrow">{t("admin.eyebrow")}</span><h1>{t("admin.crews.title")}</h1><p>{t("admin.crews.description")}</p></div><div className="hero-actions">{auth.user?.role==="SUPER_ADMIN"&&<select value={effectiveGroupId??""} onChange={event=>setSelectedGroupId(event.target.value)} aria-label={t("groups.select")}>{groups.data?.map(group=><option value={group.id} key={group.id}>{group.name}</option>)}</select>}<button className="button primary" disabled={!effectiveGroupId} onClick={() => setEdit("new")}><Plus/> {t("admin.crews.new")}</button></div></section>
    <section className="panel"><div className="admin-crew-list">{query.data?.map(crew => <article key={crew.id} className={!crew.enabled ? "disabled-row" : ""}>
      <div className="crew-list-main"><CrewIdentity number={crew.number} name={crew.name} color={crew.color} size="large"/><small>{t("admin.crews.summary", { batteries: crew.batteryCount, credentials: crew.userCount ?? 0 })}</small></div>
      <div className="crew-actions"><Link className="button compact crew-primary-action" to={`/admin/crews/${crew.id}`}><BatteryMedium/>{t("common.open")}</Link><details className="row-overflow"><summary className="icon-button" aria-label={t("admin.crews.moreActions")}><MoreHorizontal/></summary><div className="row-overflow-menu"><button onClick={() => setCredentials(crew)}><KeyRound/>{t("admin.crews.credentials")}</button><button onClick={() => setEdit(crew)}><Pencil/>{t("common.edit")}</button><button onClick={() => status.mutate({ crew, enabled: !crew.enabled })}><Power/>{crew.enabled ? t("admin.crews.disable") : t("admin.crews.enable")}</button><button className="destructive" onClick={() => confirm(t("admin.crews.deleteConfirm", { name: crew.name })) && remove.mutate(crew.id)}><Trash2/>{t("admin.crews.delete")}</button></div></details></div>
    </article>)}</div>{(status.error || remove.error) && <p className="admin-error">{t("errors.generic")}</p>}</section>
    {edit && <CrewForm groupId={effectiveGroupId} crew={edit === "new" ? undefined : edit} onClose={() => setEdit(null)}/>} {credentials && <Credentials crew={credentials} onClose={() => setCredentials(null)}/>} 
  </div>;
}

function BatteryTypeForm({ type, onClose }: { type?: BatteryType; onClose: () => void }) {
  const { t } = useI18n(); const qc = useQueryClient();
  const mutation = useMutation({ mutationFn: (data: any) => type ? api.updateBatteryType(type.id, data) : api.createBatteryType(data), onSuccess: () => { qc.invalidateQueries({ queryKey: ["battery-types"] }); onClose(); } });
  const submit = (event: FormEvent<HTMLFormElement>) => { event.preventDefault(); const data = new FormData(event.currentTarget); mutation.mutate({ name: data.get("name"), capacityAh: Number(data.get("capacityAh")), minVoltage: Number(data.get("minVoltage")), maxVoltage: Number(data.get("maxVoltage")), cellCount: Number(data.get("cellCount")), chemistry: data.get("chemistry") }); };
  return <Modal title={type ? t("batteryTypes.edit") : t("batteryTypes.create")} eyebrow={t("batteryTypes.eyebrow")} onClose={onClose}><form className="form-grid" onSubmit={submit}>
    <label className="full">{t("batteryTypes.name")}<input name="name" defaultValue={type?.name} required/></label>
    <label>{t("batteryTypes.capacity")}<input name="capacityAh" type="number" min="0.01" step="0.01" defaultValue={type?.capacityAh} required/></label>
    <label>{t("batteryTypes.cells")}<input name="cellCount" type="number" min="1" max="48" step="1" defaultValue={type?.cellCount} required/></label>
    <label>{t("batteryTypes.minVoltage")}<input name="minVoltage" type="number" min="0.01" max="1000" step="0.01" defaultValue={type?.minVoltage.toFixed(2)} required/></label>
    <label>{t("batteryTypes.maxVoltage")}<input name="maxVoltage" type="number" min="0.01" max="1000" step="0.01" defaultValue={type?.maxVoltage.toFixed(2)} required/></label>
    <label className="full">{t("batteryTypes.chemistry")}<input name="chemistry" defaultValue={type?.chemistry} required/></label>
    {mutation.error && <p className="form-error">{t("errors.generic")}</p>}<div className="form-actions full"><button type="button" className="button secondary" onClick={onClose}>{t("common.cancel")}</button><button className="button primary" disabled={mutation.isPending}>{t("common.save")}</button></div>
  </form></Modal>;
}

export function AdminBatteryTypes() {
  const { t } = useI18n(); const qc = useQueryClient(); const query = useQuery({ queryKey: ["battery-types"], queryFn: api.batteryTypes });
  const [editing, setEditing] = useState<BatteryType | "new" | null>(null);
  const remove = useMutation({ mutationFn: api.deleteBatteryType, onSuccess: () => qc.invalidateQueries({ queryKey: ["battery-types"] }) });
  return <div className="page"><section className="hero-row"><div><span className="eyebrow">{t("batteryTypes.eyebrow")}</span><h1>{t("batteryTypes.title")}</h1><p>{t("batteryTypes.description")}</p></div><button className="button primary" onClick={() => setEditing("new")}><Plus/> {t("batteryTypes.new")}</button></section>
    <section className="panel"><div className="battery-type-list">{query.data?.map(type => <article key={type.id}><span className="type-icon"><Layers3/></span><div className="type-main"><strong>{type.name}</strong><small>{type.capacityAh} {t("common.ampHours")} · {type.cellCount}S · {type.chemistry}</small></div><div className="type-voltage"><small>{t("batteryTypes.voltageRange")}</small><strong>{type.minVoltage.toFixed(2)}–{type.maxVoltage.toFixed(2)} {t("common.volts")}</strong></div><span className="type-usage">{t("batteryTypes.usedBy", { count: type.batteryCount ?? 0 })}</span><div className="crew-actions"><button className="icon-button" onClick={() => setEditing(type)} aria-label={t("common.edit")} data-tooltip={t("common.edit")}><Pencil/></button><button className="icon-button destructive" disabled={Boolean(type.batteryCount)} onClick={() => confirm(t("batteryTypes.deleteConfirm", { name: type.name })) && remove.mutate(type.id)} aria-label={t("common.delete")} data-tooltip={type.batteryCount ? t("batteryTypes.inUse") : t("common.delete")}><Trash2/></button></div></article>)}</div>{query.isLoading && <div className="empty">{t("dashboard.loading")}</div>}{!query.isLoading && !query.data?.length && <div className="empty">{t("batteryTypes.empty")}</div>}{(query.error || remove.error) && <p className="admin-error">{t("errors.generic")}</p>}</section>
    {editing && <BatteryTypeForm type={editing === "new" ? undefined : editing} onClose={() => setEditing(null)}/>} 
  </div>;
}
