import { useState, type FormEvent } from "react";
import { BatteryCharging, Eye, EyeOff, LockKeyhole, UserRound } from "lucide-react";
import { useNavigate } from "react-router-dom";
import { useAuth } from "../auth";
import { useI18n } from "../i18n";

export function Login() {
  const auth = useAuth(); const { t }=useI18n(); const navigate = useNavigate(); const [error, setError] = useState(""); const [busy, setBusy] = useState(false); const [showPassword, setShowPassword] = useState(false);
  const submit = async (e: FormEvent<HTMLFormElement>) => { e.preventDefault(); setBusy(true); setError(""); const data = new FormData(e.currentTarget); try { const user = await auth.login(String(data.get("username")), String(data.get("password"))); navigate(user.role !== "CREW" ? "/admin" : "/", { replace: true }); } catch { setError(t("login.failed")); } finally { setBusy(false); } };
  return <main className="login-page"><section className="login-card"><div className="login-brand"><span className="brand-mark"><BatteryCharging /></span><div><h1>{t("app.name")}</h1><p>{t("app.subtitle")}</p></div></div><form onSubmit={submit}><label><span>{t("login.username")}</span><div><UserRound/><input name="username" autoComplete="username" required autoFocus /></div></label><label><span>{t("login.password")}</span><div><LockKeyhole/><input name="password" type={showPassword ? "text" : "password"} autoComplete="current-password" minLength={8} required /><button type="button" className="password-toggle" onClick={() => setShowPassword(value => !value)} aria-label={showPassword ? t("common.hidePassword") : t("common.showPassword")}>{showPassword ? <EyeOff/> : <Eye/>}</button></div></label>{error&&<p className="form-error">{error}</p>}<button className="button primary" disabled={busy}>{busy?t("login.authenticating"):t("login.signIn")}</button></form><small className="login-foot">{t("login.footnote")}</small></section></main>;
}
