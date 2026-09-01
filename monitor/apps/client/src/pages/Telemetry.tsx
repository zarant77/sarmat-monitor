import { useEffect, useState, type CSSProperties } from "react";
import { createPortal } from "react-dom";
import { useQuery } from "@tanstack/react-query";
import { ExternalLink, RadioTower } from "lucide-react";
import type { TelemetryResponse, TelemetryThresholds } from "@sbm/shared";
import { api } from "../api";
import { useAuth } from "../auth";
import { useI18n } from "../i18n";
import { CrewIdentity } from "../components/CrewIdentity";

const dash = "—";
const value = (number: number | null, digits: number, suffix = "") => number === null ? dash : `${number.toFixed(digits)}${suffix}`;
const minimumClass = (number: number | null, threshold: { goodMin: number; normalMin: number }) =>
  number === null ? "" : number >= threshold.goodMin ? "telemetry-good" : number >= threshold.normalMin ? "telemetry-normal" : "telemetry-bad";
const maximumClass = (number: number | null, threshold: { goodMax: number; normalMax: number }) =>
  number === null ? "" : number <= threshold.goodMax ? "telemetry-good" : number <= threshold.normalMax ? "telemetry-normal" : "telemetry-bad";

function useTelemetry(groupId?: string) {
  return useQuery({
    queryKey: ["telemetry", groupId], queryFn: () => api.telemetry(groupId), enabled: Boolean(groupId),
    refetchInterval: 1000, refetchIntervalInBackground: true
  });
}

function TelemetryTable({ data }: { data?: TelemetryResponse }) {
  const { t } = useI18n();
  const thresholds: TelemetryThresholds | undefined = data?.thresholds;
  return <div className="telemetry-table-wrap"><table className="telemetry-table">
    <thead><tr><th>{t("telemetry.columns.crew")}</th><th>{t("telemetry.columns.status")}</th><th>{t("telemetry.columns.voltage")}</th><th>{t("telemetry.columns.current")}</th><th>Sat</th><th>HDOP</th><th>{t("telemetry.columns.heading")}</th><th>{t("telemetry.columns.altitude")}</th><th>{t("telemetry.columns.link")}</th><th>OBS</th></tr></thead>
    <tbody>{data?.crews.map(crew => {
      if (!crew.snapshot) return <tr key={crew.id}><td><div className="telemetry-crew"><CrewIdentity number={crew.number} name={crew.name} color={crew.color}/></div></td>{Array.from({ length: 9 }, (_, index) => <td key={index}>{dash}</td>)}</tr>;
      const [status, ageMs, , voltage, current, satellites, hdop, heading, altitude, linkRssi, flags] = crew.snapshot;
      const armed = Boolean(flags & 2); const recording = Boolean(flags & 1);
      return <tr key={crew.id} className={status !== 0 ? "telemetry-stale" : ""} title={t("telemetry.age", { seconds: Math.floor(ageMs / 1000) })}>
        <td><div className="telemetry-crew"><CrewIdentity number={crew.number} name={crew.name} color={crew.color}/></div></td>
        <td className={armed ? "telemetry-armed" : "telemetry-good"}>{armed ? t("telemetry.armed") : t("telemetry.disarmed")}</td>
        <td className={thresholds ? minimumClass(voltage, thresholds.voltage) : ""}>{value(voltage, 1, " V")}</td>
        <td className={thresholds ? maximumClass(current, thresholds.current) : ""}>{value(current, 1, " A")}</td>
        <td className={thresholds ? minimumClass(satellites, thresholds.satellites) : ""}>{satellites ?? dash}</td>
        <td className={thresholds ? maximumClass(hdop, thresholds.hdop) : ""}>{value(hdop, 2)}</td>
        <td>{value(heading, 0, "°")}</td><td>{value(altitude, 0, " m")}</td>
        <td className={thresholds ? minimumClass(linkRssi, thresholds.linkRssi) : ""}>{linkRssi === null ? dash : `${linkRssi} dBm`}</td>
        <td className={armed ? recording ? "telemetry-good" : "telemetry-bad" : recording ? "telemetry-normal" : "telemetry-good"}>{recording ? "REC" : "NR"}</td>
      </tr>;
    })}</tbody>
  </table></div>;
}

function TelemetryCards({ data }: { data?: TelemetryResponse }) {
  const { t } = useI18n();
  return <div className="telemetry-card-grid">{data?.crews.map(crew => {
    if (!crew.snapshot) return <article className="telemetry-status-card offline" style={{ "--crew-color": crew.color } as CSSProperties} key={crew.id}><div className="telemetry-card-head"><CrewIdentity number={crew.number} name={crew.name} color={crew.color}/><strong>{t("admin.dashboard.offline")}<i/></strong></div><div className="telemetry-no-signal"><RadioTower/>{t("telemetry.noSignal")}</div></article>;
    const [status, ageMs, , voltage, current, satellites, hdop, heading, altitude, linkRssi, flags] = crew.snapshot;
    const armed = Boolean(flags & 2); const recording = Boolean(flags & 1); const online = status === 0;
    const linkPercent = linkRssi === null ? 0 : Math.max(0, Math.min(100, Math.round((linkRssi + 100) / 60 * 100)));
    return <article className={`telemetry-status-card ${online ? "online" : "offline"}`} style={{ "--crew-color": crew.color } as CSSProperties} key={crew.id} title={t("telemetry.age", { seconds: Math.floor(ageMs / 1000) })}>
      <div className="telemetry-card-head"><CrewIdentity number={crew.number} name={crew.name} color={crew.color}/><strong>{online ? t("admin.dashboard.online") : t("admin.dashboard.offline")}<i/></strong></div>
      <div className="telemetry-card-metrics"><span><small>{t("telemetry.columns.voltage")}</small><strong>{value(voltage, 1, " V")}</strong></span><span><small>{t("telemetry.columns.current")}</small><strong>{value(current, 1, " A")}</strong></span><span><small>SAT</small><strong>{satellites ?? dash}</strong></span><span><small>HDOP</small><strong>{value(hdop, 1)}</strong></span><span><small>{t("telemetry.columns.heading")}</small><strong>{value(heading, 0, "°")}</strong></span><span><small>{t("telemetry.columns.altitude")}</small><strong>{value(altitude, 0, " m")}</strong></span></div>
      <div className="telemetry-card-footer"><div><span>{t("telemetry.columns.link")}</span><strong>{linkPercent}%</strong><i><b style={{ width: `${linkPercent}%` }}/></i></div><span className={armed ? "armed" : ""}>{armed ? t("telemetry.armed") : t("telemetry.disarmed")}</span><span className={recording ? "recording" : ""}>{recording ? "REC" : "NR"}</span></div>
    </article>;
  })}</div>;
}

export function TelemetryPage() {
  const { t } = useI18n(); const auth = useAuth(); const superAdmin = auth.user?.role === "SUPER_ADMIN";
  const groups = useQuery({ queryKey: ["groups", "telemetry"], queryFn: api.groups, enabled: superAdmin });
  const [selectedGroupId, setSelectedGroupId] = useState("");
  const groupId = superAdmin ? selectedGroupId || groups.data?.[0]?.id : auth.user?.groupId ?? undefined;
  const query = useTelemetry(groupId);
  const [detachedWindow, setDetachedWindow] = useState<Window | null>(null);
  useEffect(() => () => detachedWindow?.close(), [detachedWindow]);
  const detachedHeight = Math.max(80, Math.min(window.screen.availHeight - 80, 28 + (query.data?.crews.length ?? 1) * 49));
  const detach = async () => {
    if (detachedWindow && !detachedWindow.closed) { detachedWindow.focus(); return; }
    const pictureInPicture = (window as Window & { documentPictureInPicture?: { requestWindow(options: { width: number; height: number }): Promise<Window> } }).documentPictureInPicture;
    if (pictureInPicture) {
      try {
        const detached = await pictureInPicture.requestWindow({ width: 1280, height: detachedHeight });
        document.head.querySelectorAll('link[rel="stylesheet"], style').forEach(node => detached.document.head.append(node.cloneNode(true)));
        detached.document.title = t("telemetry.title");
        detached.addEventListener("pagehide", () => setDetachedWindow(null), { once: true });
        setDetachedWindow(detached);
        return;
      } catch {
        // Fall back to a regular popup when Document Picture-in-Picture is unavailable or denied.
      }
    }
    const search = groupId ? `?groupId=${encodeURIComponent(groupId)}` : "";
    window.open(`/telemetry-detached${search}`, "sarmat-telemetry", `popup=yes,width=1280,height=${detachedHeight},resizable=yes,scrollbars=yes,location=no,toolbar=no,menubar=no,status=no`);
  };

  return <div className="page telemetry-page">
    <section className="hero-row telemetry-hero"><div><span className="eyebrow">{t("telemetry.eyebrow")}</span><h1>{t("telemetry.title")}</h1><p>{t("telemetry.description")}</p></div><div className="hero-actions">
      {superAdmin && <select value={groupId ?? ""} onChange={event => setSelectedGroupId(event.target.value)} aria-label={t("groups.select")}>{groups.data?.map(group => <option value={group.id} key={group.id}>{group.name}</option>)}</select>}
      <span className={`telemetry-connection ${query.isError ? "offline" : ""}`}><i/>{query.isError ? t("telemetry.connectionLost") : t("telemetry.updating")}</span>
      <button className="button secondary" type="button" onClick={detach} disabled={!groupId}><ExternalLink/>{t("telemetry.detach")}</button>
    </div></section>
    <section className="telemetry-cards-panel"><TelemetryCards data={query.data}/>{query.isLoading && <div className="empty">{t("telemetry.loading")}</div>}{!query.isLoading && !query.data?.crews.length && <div className="empty"><RadioTower/>{t("telemetry.empty")}</div>}{query.isError && <p className="admin-error">{t("telemetry.loadError")}</p>}</section>
    {query.data?.crews.length ? <details className="panel telemetry-table-panel"><summary>{t("telemetry.tableView")}</summary><TelemetryTable data={query.data}/></details> : null}
    {detachedWindow && createPortal(<main className="detached-telemetry"><TelemetryTable data={query.data}/></main>, detachedWindow.document.body)}
  </div>;
}

export function DetachedTelemetryPage() {
  const auth = useAuth();
  const requestedGroupId = new URLSearchParams(window.location.search).get("groupId") ?? undefined;
  const groupId = auth.user?.role === "SUPER_ADMIN" ? requestedGroupId : auth.user?.groupId ?? undefined;
  const query = useTelemetry(groupId);
  return <main className="detached-telemetry"><TelemetryTable data={query.data}/></main>;
}
