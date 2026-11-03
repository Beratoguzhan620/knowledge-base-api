import { BookOpen, Check, ChevronsUpDown, FileText, LogOut, MessageSquare, Plus, Search, Users } from "lucide-react";
import { useEffect, useRef, useState, type FormEvent, type ReactNode } from "react";
import { api } from "../api";
import { useSession, useWorkspace } from "../context";
import { initials } from "../format";
import { linkProps, navigate, type Route } from "../router";
import { Dialog } from "./Dialog";

function NavLink({ href, active, icon, children }: { href: string; active: boolean; icon: ReactNode; children: ReactNode }) {
  return (
    <a {...linkProps(href)} className={`nav-link${active ? " active" : ""}`} aria-current={active ? "page" : undefined}>
      {icon}
      {children}
    </a>
  );
}

export function NewWorkspaceDialog({ onClose }: { onClose: () => void }) {
  const { refresh } = useSession();
  const [name, setName] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function submit(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const workspace = await api.createWorkspace(name);
      await refresh();
      onClose();
      navigate(`/w/${workspace.id}/documents`);
    } catch (err) {
      setError((err as Error).message);
      setBusy(false);
    }
  }

  return (
    <Dialog title="New workspace" onClose={onClose}>
      <form className="stack" onSubmit={submit}>
        <label className="field">
          Name
          <input className="input" value={name} onChange={(e) => setName(e.target.value)} placeholder="Support team" autoFocus required minLength={2} maxLength={60} />
          <span className="hint">You become its owner and can add your team from Members.</span>
        </label>
        {error && <p className="form-error" role="alert">{error}</p>}
        <div className="dialog-actions">
          <button type="button" className="button" onClick={onClose}>Cancel</button>
          <button className="button primary" disabled={busy || name.trim().length < 2}>Create workspace</button>
        </div>
      </form>
    </Dialog>
  );
}

function WorkspaceSwitcher() {
  const { me } = useSession();
  const current = useWorkspace();
  const [open, setOpen] = useState(false);
  const [creating, setCreating] = useState(false);
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) return;
    const close = (e: MouseEvent) => { if (!ref.current?.contains(e.target as Node)) setOpen(false); };
    document.addEventListener("mousedown", close);
    return () => document.removeEventListener("mousedown", close);
  }, [open]);

  return (
    <div className="ws-switch" ref={ref}>
      <button className="ws-button" onClick={() => setOpen(!open)} aria-expanded={open} aria-haspopup="menu" aria-label={`Workspace: ${current.name}`}>
        <span className="ws-avatar">{initials(current.name)}</span>
        <span className="grow">
          <span className="ws-name">{current.name}</span>
          <span className="small muted">{current.role} · {current.documents} document{current.documents === 1 ? "" : "s"}</span>
        </span>
        <ChevronsUpDown size={16} className="muted" />
      </button>
      {open && (
        <div className="ws-menu" role="menu">
          {me.workspaces.map((w) => (
            <button key={w.id} role="menuitem" className={`menu-item${w.id === current.id ? " active" : ""}`}
              onClick={() => { setOpen(false); navigate(`/w/${w.id}`); }}>
              <span className="ws-avatar small">{initials(w.name)}</span>
              <span className="grow ellipsis">{w.name}</span>
              {w.id === current.id && <Check size={15} />}
            </button>
          ))}
          <div className="menu-divider" />
          <button role="menuitem" className="menu-item" onClick={() => { setOpen(false); setCreating(true); }}>
            <Plus size={16} /> New workspace
          </button>
        </div>
      )}
      {creating && <NewWorkspaceDialog onClose={() => setCreating(false)} />}
    </div>
  );
}

export function Shell({ route, children }: { route: Route; children: ReactNode }) {
  const { session, signOut } = useSession();
  const workspace = useWorkspace();
  const base = `/w/${workspace.id}`;
  const page = route.page;

  return (
    <div className="shell">
      <nav className="nav" aria-label="Main">
        <div className="nav-top">
          <div className="brand small"><span className="brand-mark small"><BookOpen size={14} /></span>Knowledge Base</div>
          <WorkspaceSwitcher />
        </div>
        <div className="nav-links">
          <NavLink href={base} active={page === "ask"} icon={<MessageSquare size={17} />}>Ask</NavLink>
          <NavLink href={`${base}/documents`} active={page === "documents" || page === "document"} icon={<FileText size={17} />}>Documents</NavLink>
          <NavLink href={`${base}/search`} active={page === "search"} icon={<Search size={17} />}>Search</NavLink>
          <NavLink href={`${base}/members`} active={page === "members"} icon={<Users size={17} />}>Members</NavLink>
        </div>
        <div className="nav-bottom">
          <span className="avatar">{initials(session.user.displayName)}</span>
          <span className="grow">
            <span className="ellipsis strong-14">{session.user.displayName}</span>
            <span className="ellipsis small muted block">{session.user.email}</span>
          </span>
          <button className="icon-button" onClick={signOut} aria-label="Sign out" title="Sign out"><LogOut size={17} /></button>
        </div>
      </nav>
      <main className={`content${page === "ask" ? " wide" : ""}`}>{children}</main>
    </div>
  );
}
