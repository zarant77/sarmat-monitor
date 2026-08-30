import { useEffect, useState } from "react";
import { Activity, BatteryCharging, Gauge, Layers3, LayoutDashboard, LogOut, Maximize, Minimize, Monitor, RadioTower, Settings, Shield, Users } from "lucide-react";
import { NavLink, Navigate, Route, Routes, useLocation } from "react-router-dom";
import { useAuth } from "./auth";
import { Dashboard } from "./pages/Dashboard";
import { BatteryDetails } from "./pages/BatteryDetails";
import { SettingsPage } from "./pages/Settings";
import { Login } from "./pages/Login";
import { AdminBatteryTypes, AdminDashboard, AdminGroupDetails, AdminGroups, AdminCrews } from "./pages/Admin";
import { useI18n } from "./i18n";
import { DetachedTelemetryPage, TelemetryPage } from "./pages/Telemetry";

function Shell() {
  const auth = useAuth(); const { t } = useI18n(); const role = auth.user!.role; const admin = role !== "CREW"; const superAdmin = role === "SUPER_ADMIN";
  const [fullscreen, setFullscreen] = useState(Boolean(document.fullscreenElement));
  useEffect(() => { const update = () => setFullscreen(Boolean(document.fullscreenElement)); document.addEventListener("fullscreenchange", update); return () => document.removeEventListener("fullscreenchange", update); }, []);
  const toggleFullscreen = () => document.fullscreenElement ? document.exitFullscreen() : document.documentElement.requestFullscreen();
  const fullscreenLabel = fullscreen ? t("nav.exitFullscreen") : t("nav.fullscreen");
  return <div className="app-shell"><header className="topbar"><NavLink to={admin?"/admin":"/"} className="brand" aria-label="SARMAT monitor"><span className="brand-mark brand-monitor-mark"><Monitor aria-hidden="true"/></span><span className="brand-copy"><strong className="brand-wordmark">SARMAT</strong><small className="brand-product">monitor</small></span></NavLink><nav aria-label={t("nav.primary")}>{admin?<><NavLink to="/admin" end><LayoutDashboard size={18}/> {t("nav.dashboard")}</NavLink>{superAdmin&&<NavLink to="/admin/groups"><Shield size={18}/> {t("nav.groups")}</NavLink>}<NavLink to="/admin/crews"><Users size={18}/> {t("nav.crews")}</NavLink><NavLink to="/admin/telemetry"><RadioTower size={18}/> {t("nav.telemetry")}</NavLink><NavLink to="/admin/batteries"><Gauge size={18}/> {t("nav.batteries")}</NavLink>{superAdmin&&<><NavLink to="/admin/battery-types"><Layers3 size={18}/> {t("nav.batteryTypes")}</NavLink><NavLink to="/admin/settings"><Settings size={18}/> {t("nav.settings")}</NavLink></>}</>:<NavLink to="/" end><Activity size={18}/> {t("nav.fleet")}</NavLink>}</nav><div className="account-chip"><span><strong>{auth.user!.username}</strong><small>{role === "SUPER_ADMIN" ? t("nav.superAdministrator") : role === "GROUP_ADMIN" ? auth.user!.groupName : `№${auth.user!.crewNumber} · ${auth.user!.crewName}`}</small></span><button className="header-icon-button" type="button" onClick={toggleFullscreen} title={fullscreenLabel} aria-label={fullscreenLabel}>{fullscreen ? <Minimize/> : <Maximize/>}</button><button className="header-icon-button" onClick={auth.logout} aria-label={t("nav.signOut")} title={t("nav.signOut")}><LogOut/></button></div></header><main><Routes>{admin?<><Route path="/admin" element={<AdminDashboard/>}/>{superAdmin&&<><Route path="/admin/groups" element={<AdminGroups/>}/><Route path="/admin/groups/:groupId" element={<AdminGroupDetails/>}/><Route path="/admin/battery-types" element={<AdminBatteryTypes/>}/><Route path="/admin/settings" element={<SettingsPage/>}/></>}<Route path="/admin/crews" element={<AdminCrews/>}/><Route path="/admin/telemetry" element={<TelemetryPage/>}/><Route path="/admin/crews/:crewId" element={<Dashboard adminMode/>}/><Route path="/admin/batteries" element={<Dashboard adminMode/>}/><Route path="/admin/batteries/:id" element={<BatteryDetails/>}/><Route path="/batteries/:id" element={<BatteryDetails/>}/><Route path="*" element={<Navigate to="/admin" replace/>}/></>:<><Route path="/" element={<Dashboard/>}/><Route path="/batteries/:id" element={<BatteryDetails/>}/><Route path="*" element={<Navigate to="/" replace/>}/></>}</Routes></main></div>;
}

export function App() {
  const auth=useAuth(); const location=useLocation(); const { t }=useI18n();
  if(auth.loading) return <div className="app-loading"><span className="brand-mark"><BatteryCharging/></span><p>{t("app.loading")}</p></div>;
  if(!auth.user) return location.pathname==="/login"?<Login/>:<Navigate to="/login" replace/>;
  if(location.pathname==="/login") return <Navigate to={auth.user.role!=="CREW"?"/admin":"/"} replace/>;
  if(location.pathname==="/telemetry-detached") return auth.user.role==="CREW"?<Navigate to="/" replace/>:<DetachedTelemetryPage/>;
  return <Shell/>;
}
