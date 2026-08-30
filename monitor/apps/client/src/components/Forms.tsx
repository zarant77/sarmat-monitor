import { useState, type FormEvent } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { Battery, Crew } from "@sbm/shared";
import { api } from "../api";
import { Modal } from "./Modal";
import { useI18n } from "../i18n";

function ErrorText({ error }: { error: unknown }) { const { t }=useI18n(); return error ? <p className="form-error">{t("errors.generic")}</p> : null; }

export function CrewForm({ crew, groupId, onClose }: { crew?: Crew; groupId?: string; onClose: () => void }) {
  const qc = useQueryClient(); const { t }=useI18n();
  const [color, setColor] = useState(crew?.color ?? "#B7EF55");
  const pickerColor = /^#[0-9A-Fa-f]{6}$/.test(color) ? color : "#B7EF55";
  const mutation = useMutation({ mutationFn: (data: { groupId?: string; number: number; name: string; color: string; secret: string; notes: string; enabled: boolean; reserve: boolean }) => { const { groupId: _groupId, ...update } = data; return crew ? api.updateCrew(crew.id, update) : api.createCrew(data); }, onSuccess: () => { qc.invalidateQueries({ queryKey: ["crews"] }); qc.invalidateQueries({ queryKey: ["groups"] }); onClose(); } });
  const submit = (e: FormEvent<HTMLFormElement>) => { e.preventDefault(); const d = new FormData(e.currentTarget); mutation.mutate({ groupId, number: Number(d.get("number")), name: String(d.get("name")), color: color.toUpperCase(), secret: String(d.get("secret")), notes: String(d.get("notes")), enabled: crew?.enabled ?? true, reserve: d.get("reserve") === "on" }); };
  return <Modal title={crew ? t("forms.editCrew") : t("forms.createCrew")} eyebrow={t("forms.fleetStructure")} onClose={onClose}><form onSubmit={submit} className="form-grid">
    <label>{t("forms.crewNumber")}<input name="number" type="number" min="1" max="9999" step="1" defaultValue={crew?.number} required placeholder={t("forms.crewNumberPlaceholder")} /></label>
    <label>{t("forms.crewName")}<input name="name" defaultValue={crew?.name} required placeholder={t("forms.crewNamePlaceholder")} /></label>
    <label>{t("forms.crewColor")}<span className="color-picker-field"><input type="color" value={pickerColor} onChange={event => setColor(event.target.value.toUpperCase())}/><input className="color-hex-input" name="color" type="text" value={color} onChange={event => setColor(event.target.value)} onBlur={() => setColor(value => value.toUpperCase())} pattern="#[0-9A-Fa-f]{6}" maxLength={7} required aria-label={t("forms.crewColorHex")}/></span></label>
    <label className="checkbox-label"><input name="reserve" type="checkbox" defaultChecked={crew?.reserve}/>{t("forms.reserveCrew")}</label>
    <label className="full">{t("forms.telemetrySecret")}<input name="secret" type="text" defaultValue={crew?.secret} maxLength={200} placeholder={t("forms.telemetrySecretPlaceholder")} autoComplete="off"/><small>{t("forms.telemetrySecretHelp")}</small></label>
    <label className="full">{t("common.notes")}<textarea name="notes" defaultValue={crew?.notes} placeholder={t("forms.crewNotesPlaceholder")} /></label>
    <ErrorText error={mutation.error} /><div className="form-actions full"><button type="button" className="button secondary" onClick={onClose}>{t("common.cancel")}</button><button className="button primary" disabled={mutation.isPending}>{mutation.isPending ? t("common.saving") : t("forms.saveCrew")}</button></div>
  </form></Modal>;
}

export function BatteryForm({ crewId, battery, onClose }: { crewId?: string; battery?: Battery; onClose: () => void }) {
  const qc = useQueryClient(); const { t }=useI18n(); const types = useQuery({ queryKey: ["battery-types"], queryFn: api.batteryTypes }); const mutation = useMutation({ mutationFn: (data: any) => battery ? api.updateBattery(battery.id, data) : api.createBattery(data), onSuccess: () => { qc.invalidateQueries({ queryKey: ["batteries"] }); qc.invalidateQueries({ queryKey: ["battery-types"] }); if (battery) qc.invalidateQueries({ queryKey: ["battery", battery.id] }); onClose(); } });
  const submit = (e: FormEvent<HTMLFormElement>) => { e.preventDefault(); const d = new FormData(e.currentTarget); mutation.mutate({ ...(battery ? {} : { crewId }), label: d.get("label"), serialNumber: d.get("serialNumber"), typeId: d.get("typeId"), state: d.get("state"), notes: d.get("notes") }); };
  return <Modal title={battery ? t("forms.editBattery") : t("forms.registerBattery")} eyebrow={t("forms.packIdentity")} onClose={onClose}><form onSubmit={submit} className="form-grid">
    <label>{t("forms.fieldLabel")}<input name="label" defaultValue={battery?.label} required placeholder={t("forms.fieldLabelPlaceholder")} /></label><label>{t("forms.serialNumber")}<input name="serialNumber" defaultValue={battery?.serialNumber} required placeholder={t("forms.serialPlaceholder")} /></label>
    <label>{t("forms.batteryType")}<select name="typeId" defaultValue={battery?.typeId ?? ""} required><option value="">{t("forms.selectBatteryType")}</option>{types.data?.map(type => <option value={type.id} key={type.id}>{type.name}</option>)}</select></label><label>{t("forms.operationalState")}<select name="state" defaultValue={battery?.state ?? "ready"}><option value="ready">{t("states.ready")}</option><option value="charging">{t("states.charging")}</option><option value="in_use">{t("states.in_use")}</option><option value="storage">{t("states.storage")}</option><option value="service">{t("states.service")}</option><option value="retired">{t("states.retired")}</option></select></label>
    <label className="full">{t("common.notes")}<textarea name="notes" defaultValue={battery?.notes} /></label>{!types.isLoading && !types.data?.length && <p className="form-error full">{t("forms.noBatteryTypes")}</p>}<ErrorText error={mutation.error ?? types.error} /><div className="form-actions full"><button type="button" className="button secondary" onClick={onClose}>{t("common.cancel")}</button><button className="button primary" disabled={mutation.isPending || !types.data?.length}>{t("forms.saveBattery")}</button></div>
  </form></Modal>;
}
