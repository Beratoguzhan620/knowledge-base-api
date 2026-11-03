import { AlertTriangle, CheckCircle2, Clock, Loader2 } from "lucide-react";
import { statusLabel } from "../format";
import type { DocumentStatus, Role } from "../types";

const statusIcons = { Queued: Clock, Indexing: Loader2, Ready: CheckCircle2, Failed: AlertTriangle };

export function StatusBadge({ status, error }: { status: DocumentStatus; error?: string | null }) {
  const Icon = statusIcons[status];
  return (
    <span className={`badge status-${status}`} title={error ?? undefined}>
      <Icon size={12} className={status === "Indexing" ? "spin" : undefined} />
      {statusLabel[status]}
    </span>
  );
}

export function RoleBadge({ role }: { role: Role }) {
  return <span className={`badge ${role === "Owner" ? "accent" : "plain"}`}>{role}</span>;
}
