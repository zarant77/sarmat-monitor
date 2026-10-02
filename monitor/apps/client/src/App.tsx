import { useEffect, useRef, useState, type ReactNode } from "react";
import { BatteryCharging, BatteryMedium, ChevronDown, Cog, Gauge, Globe2, Layers3, LayoutDashboard, LogOut, Maximize, Minimize, Monitor, Plane, RadioTower, Settings, Shield, Users } from "lucide-react";
import { NavLink, Navigate, Route, Routes, useLocation } from "react-router-dom";
import { useQuery } from "@tanstack/react-query";
import { useAuth } from "./auth";
import { Dashboard } from "./pages/Dashboard";
import { BatteryDetails } from "./pages/BatteryDetails";
import { SettingsPage } from "./pages/Settings";
import { Login } from "./pages/Login";
import { AdminBatteryTypes, AdminDashboard, AdminGroupDetails, AdminGroups, AdminCrews } from "./pages/Admin";
import { useI18n } from "./i18n";
import { DetachedTelemetryPage, TelemetryPage } from "./pages/Telemetry";
import { api } from "./api";
import { AdminDrones, AdminMotors } from "./pages/Equipment";
import { DroneDetails, MotorDetails } from "./pages/EquipmentDetails";

function NavDropdown({ label, active, children }: { label: string; active: boolean; children: ReactNode }) {
  const [open, setOpen] = useState(false);
  const root = useRef<HTMLDivElement>(null);
  const location = useLocation();
  useEffect(() => { setOpen(false); }, [location]);
  useEffect(() => {
    if (!open) return;
    const dismiss = (event: PointerEvent) => { if (!root.current?.contains(event.target as Node)) setOpen(false); };
    const escape = (event: KeyboardEvent) => { if (event.key === "Escape") { setOpen(false); root.current?.querySelector("button")?.focus(); } };
    document.addEventListener("pointerdown", dismiss);
    document.addEventListener("keydown", escape);
    return () => { document.removeEventListener("pointerdown", dismiss); document.removeEventListener("keydown", escape); };
  }, [open]);
  return <div ref={root} className="nav-dropdown" onBlur={event => { if (!event.currentTarget.contains(event.relatedTarget)) setOpen(false); }}>
    <button type="button" className={active ? "active" : ""} aria-expanded={open} onClick={() => setOpen(!open)}>{label}<ChevronDown size={16}/></button>
    {open && <div className="nav-dropdown-panel" onClick={() => setOpen(false)}>{children}</div>}
  </div>;
}

function OperationalStatus({ admin }: { admin: boolean }) {
  const { t } = useI18n();
  const batteries = useQuery({ queryKey: ["batteries", "header-status"], queryFn: () => api.batteries(), enabled: admin, refetchInterval: 30000 });
  if (!admin) return null;
  const batteryIssues = batteries.data?.filter(item => item.latestMeasurement && item.latestMeasurement.health !== "good").length ?? 0;
  return <NavLink className={`system-status ${batteryIssues ? "has-issues" : ""}`} to={batteryIssues ? "/admin/batteries?attention=1" : "/admin"} title={batteryIssues ? t("status.batteryIssueCount", { count: batteryIssues }) : t("status.systemOk")}>
    <i/>{batteryIssues ? t("status.issues", { count: batteryIssues }) : t("status.systemOk")}
  </NavLink>;
}

function Shell() {
  const { pathname } = useLocation();
  const auth = useAuth(); const { t, locale, setLocale } = useI18n(); const role = auth.user!.role; const superAdmin = role === "SUPER_ADMIN";
  const [fullscreen, setFullscreen] = useState(Boolean(document.fullscreenElement));
  useEffect(() => { const update = () => setFullscreen(Boolean(document.fullscreenElement)); document.addEventListener("fullscreenchange", update); return () => document.removeEventListener("fullscreenchange", update); }, []);
  const toggleFullscreen = () => document.fullscreenElement ? document.exitFullscreen() : document.documentElement.requestFullscreen();
  const fullscreenLabel = fullscreen ? t("nav.exitFullscreen") : t("nav.fullscreen");
  return <div className="app-shell admin-shell"><header className="topbar"><NavLink to="/admin" className="brand" aria-label="SARMAT monitor"><span className="brand-mark brand-monitor-mark"><Monitor aria-hidden="true"/></span><span className="brand-copy"><strong className="brand-wordmark">SARMAT</strong><small className="brand-product">monitor</small></span></NavLink><nav aria-label={t("nav.primary")}>
      <NavLink to="/admin" end><LayoutDashboard size={18}/> {t("nav.dashboard")}</NavLink>
      <NavLink to="/admin/crews"><Users size={18}/> {t("nav.crews")}</NavLink>
      <NavDropdown label={t("nav.equipment")} active={/^\/admin\/(drones|motors|batteries)(\/|$)/.test(pathname)}>
        <NavLink to="/admin/drones"><Plane size={18}/> {t("nav.drones")}</NavLink>
        <NavLink to="/admin/motors"><Cog size={18}/> {t("nav.motors")}</NavLink>
        <NavLink to="/admin/batteries"><Gauge size={18}/> {t("nav.batteries")}</NavLink>
      </NavDropdown>
      <NavLink to="/admin/telemetry"><RadioTower size={18}/> {t("nav.telemetry")}</NavLink>
      {superAdmin && <NavDropdown label={t("nav.administration")} active={/^\/admin\/(groups|battery-types|settings)(\/|$)/.test(pathname)}>
        <NavLink to="/admin/groups"><Shield size={18}/> {t("nav.groups")}</NavLink>
        <NavLink to="/admin/battery-types"><Layers3 size={18}/> {t("nav.batteryTypes")}</NavLink>
        <NavLink to="/admin/settings"><Settings size={18}/> {t("nav.settings")}</NavLink>
      </NavDropdown>}
    </nav><OperationalStatus admin/><div className="account-chip"><span><strong>{auth.user!.username}</strong><small>{role === "SUPER_ADMIN" ? t("nav.superAdministrator") : auth.user!.groupName}</small></span><label className="language-menu" title={t("nav.language")}><Globe2 aria-hidden="true"/><select value={locale} onChange={event => setLocale(event.target.value as "uk" | "en")} aria-label={t("nav.language")}><option value="uk">UA</option><option value="en">EN</option></select></label><button className="header-icon-button" type="button" onClick={toggleFullscreen} title={fullscreenLabel} aria-label={fullscreenLabel}>{fullscreen ? <Minimize/> : <Maximize/>}</button><button className="header-icon-button" onClick={auth.logout} aria-label={t("nav.signOut")} title={t("nav.signOut")}><LogOut/></button></div></header><main><Routes><Route path="/admin" element={<AdminDashboard/>}/>{superAdmin&&<><Route path="/admin/groups" element={<AdminGroups/>}/><Route path="/admin/groups/:groupId" element={<AdminGroupDetails/>}/><Route path="/admin/battery-types" element={<AdminBatteryTypes/>}/><Route path="/admin/settings" element={<SettingsPage/>}/></>}<Route path="/admin/crews" element={<AdminCrews/>}/><Route path="/admin/drones" element={<AdminDrones/>}/><Route path="/admin/drones/:id" element={<DroneDetails/>}/><Route path="/admin/motors" element={<AdminMotors/>}/><Route path="/admin/motors/:id" element={<MotorDetails/>}/><Route path="/admin/telemetry" element={<TelemetryPage/>}/><Route path="/admin/crews/:crewId" element={<Dashboard adminMode/>}/><Route path="/admin/batteries" element={<Dashboard adminMode/>}/><Route path="/admin/batteries/:id" element={<BatteryDetails/>}/><Route path="*" element={<Navigate to="/admin" replace/>}/></Routes></main></div>;
}

function CrewShell() {
  const auth = useAuth(); const { t, locale, setLocale } = useI18n();
  const [fullscreen, setFullscreen] = useState(Boolean(document.fullscreenElement));
  useEffect(() => { const update = () => setFullscreen(Boolean(document.fullscreenElement)); document.addEventListener("fullscreenchange", update); return () => document.removeEventListener("fullscreenchange", update); }, []);
  const toggleFullscreen = () => document.fullscreenElement ? document.exitFullscreen() : document.documentElement.requestFullscreen();
  const fullscreenLabel = fullscreen ? t("nav.exitFullscreen") : t("nav.fullscreen");
  return <div className="app-shell crew-shell"><header className="topbar"><NavLink to="/" className="brand" aria-label="SARMAT Crew"><span className="brand-mark"><BatteryCharging aria-hidden="true"/></span><span className="brand-copy"><strong className="brand-wordmark">SARMAT</strong><small className="brand-product">crew</small></span></NavLink><nav aria-label={t("nav.primary")}><NavLink to="/" end><BatteryMedium size={18}/> {t("nav.batteries")}</NavLink></nav><div className="account-chip"><span><strong>{auth.user!.username}</strong><small>{auth.user!.crewNumber != null ? `№${auth.user!.crewNumber} · ${auth.user!.crewName}` : auth.user!.groupName}</small></span><label className="language-menu" title={t("nav.language")}><Globe2 aria-hidden="true"/><select value={locale} onChange={event => setLocale(event.target.value as "uk" | "en")} aria-label={t("nav.language")}><option value="uk">UA</option><option value="en">EN</option></select></label><button className="header-icon-button" type="button" onClick={toggleFullscreen} title={fullscreenLabel} aria-label={fullscreenLabel}>{fullscreen ? <Minimize/> : <Maximize/>}</button><button className="header-icon-button" onClick={auth.logout} aria-label={t("nav.signOut")} title={t("nav.signOut")}><LogOut/></button></div></header><main><Routes><Route path="/" element={<Dashboard/>}/><Route path="/batteries/:id" element={<BatteryDetails/>}/><Route path="*" element={<Navigate to="/" replace/>}/></Routes></main></div>;
}

export function App() {
  const auth=useAuth(); const location=useLocation(); const { t }=useI18n();
  if(auth.loading) return <div className="app-loading"><span className="brand-mark"><BatteryCharging/></span><p>{t("app.loading")}</p></div>;
  if(!auth.user) return location.pathname==="/login"?<Login/>:<Navigate to="/login" replace/>;
  if(location.pathname==="/login") return <Navigate to={auth.user.role === "CREW" ? "/" : "/admin"} replace/>;
  if(auth.user.role === "CREW") return <CrewShell/>;
  if(location.pathname==="/telemetry-detached") return <DetachedTelemetryPage/>;
  return <Shell/>;
}
