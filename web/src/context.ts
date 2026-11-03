import { createContext, useContext } from "react";
import type { Me, Session, Workspace } from "./types";

export interface SessionContext {
  session: Session;
  me: Me;
  /** Reads the workspaces again, after one was created, renamed, left or deleted. */
  refresh: () => Promise<Me>;
  signOut: () => void;
}

export const SessionCtx = createContext<SessionContext | null>(null);

export function useSession(): SessionContext {
  const value = useContext(SessionCtx);
  if (!value) throw new Error("useSession outside of a signed-in page");
  return value;
}

export const WorkspaceCtx = createContext<Workspace | null>(null);

/** The workspace of the current page. */
export function useWorkspace(): Workspace {
  const value = useContext(WorkspaceCtx);
  if (!value) throw new Error("useWorkspace outside of a workspace page");
  return value;
}
