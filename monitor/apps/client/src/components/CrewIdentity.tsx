import type { CSSProperties, ReactNode } from "react";

export function CrewIdentity({ number, name, color, size = "medium", suffix, className = "" }: {
  number: number;
  name: string;
  color: string;
  size?: "small" | "medium" | "large";
  suffix?: ReactNode;
  className?: string;
}) {
  return <span className={`crew-identity ${size} ${className}`.trim()}>
    <i className="crew-identity-marker" style={{ "--crew-color": color } as CSSProperties}>{number}</i>
    <span className="crew-identity-name">{name}</span>
    {suffix}
  </span>;
}
