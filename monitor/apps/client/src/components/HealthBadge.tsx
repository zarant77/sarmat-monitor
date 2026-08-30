import { AlertTriangle, CheckCircle2, ShieldAlert } from "lucide-react";
import type { HealthState } from "@sbm/shared";
import { useI18n } from "../i18n";
export function HealthBadge({ health }: { health: HealthState }) {
  const { t }=useI18n();
  const Icon = health === "danger" ? ShieldAlert : health === "warning" ? AlertTriangle : CheckCircle2;
  return <span className={`health-badge ${health}`}><Icon size={14} />{t(`health.${health}`)}</span>;
}
