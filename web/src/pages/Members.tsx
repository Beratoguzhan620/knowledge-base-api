import { LogOut, Trash2, UserPlus } from "lucide-react";
import { useState, type FormEvent } from "react";
import { api } from "../api";
import { RoleBadge } from "../components/Badges";
import { Dialog } from "../components/Dialog";
import { Failed, SkeletonRows } from "../components/States";
import { useSession, useWorkspace } from "../context";
import { initials, isOwner, relative, roleHint } from "../format";
import { useLoad } from "../hooks";
import { navigate } from "../router";
import type { Role } from "../types";

const roles: Role[] = ["Reader", "Editor", "Owner"];

function WorkspaceSettings() {
  const workspace = useWorkspace();
  const { refresh } = useSession();
  const [name, setName] = useState(workspace.name);
  const [message, setMessage] = useState<{ ok: boolean; text: string } | null>(null);
  const [deleting, setDeleting] = useState(false);
  const [confirm, setConfirm] = useState("");

  async function rename(e: FormEvent) {
    e.preventDefault();
    try {
      await api.renameWorkspace(workspace.id, name);
      await refresh();
      setMessage({ ok: true, text: "Renamed." });
    } catch (err) {
      setMessage({ ok: false, text: (err as Error).message });
    }
  }

  async function reindex() {
    const { queued } = await api.reindexWorkspace(workspace.id, true);
    setMessage({ ok: true, text: `${queued} document${queued === 1 ? "" : "s"} queued for indexing.` });
  }

  async function remove() {
    await api.deleteWorkspace(workspace.id);
    const me = await refresh();
    navigate(me.workspaces[0] ? `/w/${me.workspaces[0].id}` : "/", true);
  }

  return (
    <section className="card section">
      <h2>Workspace</h2>
      <form className="row wrap" onSubmit={rename}>
        <input className="input grow" value={name} onChange={(e) => setName(e.target.value)} aria-label="Workspace name" maxLength={60} />
        <button className="button" disabled={name.trim() === workspace.name || name.trim().length < 2}>Rename</button>
      </form>
      <div className="row wrap">
        <button className="button" onClick={reindex}>Index every document again</button>
        <span className="small muted">After changing the embedding model.</span>
      </div>
      {message && <p className={message.ok ? "form-ok" : "form-error"} role="status">{message.text}</p>}
      <div className="danger-zone">
        <div className="grow"><strong>Delete the workspace</strong><p className="small muted">Its documents, passages and everyone's conversations go with it.</p></div>
        <button className="button danger" onClick={() => setDeleting(true)}>Delete…</button>
      </div>
      {deleting && (
        <Dialog title="Delete workspace" onClose={() => setDeleting(false)}>
          <p>This cannot be undone. Type <strong>{workspace.name}</strong> to confirm.</p>
          <input className="input" value={confirm} onChange={(e) => setConfirm(e.target.value)} aria-label="Workspace name to confirm" autoFocus />
          <div className="dialog-actions">
            <button className="button" onClick={() => setDeleting(false)}>Cancel</button>
            <button className="button danger" disabled={confirm !== workspace.name} onClick={remove}>Delete workspace</button>
          </div>
        </Dialog>
      )}
    </section>
  );
}

export function Members() {
  const workspace = useWorkspace();
  const { session, refresh } = useSession();
  const [members, reload] = useLoad(() => api.members(workspace.id), [workspace.id]);
  const [email, setEmail] = useState("");
  const [role, setRole] = useState<Role>("Reader");
  const [error, setError] = useState<string | null>(null);
  const owner = isOwner(workspace.role);

  async function add(e: FormEvent) {
    e.preventDefault();
    setError(null);
    try {
      await api.addMember(workspace.id, email, role);
      setEmail("");
      reload();
      void refresh();
    } catch (err) {
      setError((err as Error).message);
    }
  }

  async function act(action: () => Promise<unknown>) {
    setError(null);
    try {
      await action();
      reload();
      void refresh();
    } catch (err) {
      setError((err as Error).message);
    }
  }

  async function leave() {
    setError(null);
    try {
      await api.removeMember(workspace.id, session.user.id);
      const me = await refresh();
      navigate(me.workspaces[0] ? `/w/${me.workspaces[0].id}` : "/", true);
    } catch (err) {
      setError((err as Error).message);
    }
  }

  return (
    <>
      <div className="page-head">
        <div>
          <h1>Members</h1>
          <p className="muted">Readers search and ask. Editors also add and delete documents. Owners also manage members and the workspace.</p>
        </div>
        <button className="button" onClick={leave}><LogOut size={15} />Leave</button>
      </div>

      {owner && (
        <form className="card section add-member" onSubmit={add}>
          <h2>Add someone</h2>
          <div className="row wrap">
            <input className="input grow" type="email" value={email} onChange={(e) => setEmail(e.target.value)} placeholder="name@company.com" aria-label="Email" required />
            <select className="select narrow" value={role} onChange={(e) => setRole(e.target.value as Role)} aria-label="Role">
              {roles.map((r) => <option key={r}>{r}</option>)}
            </select>
            <button className="button primary"><UserPlus size={15} />Add</button>
          </div>
          <span className="hint">{role}: {roleHint[role].toLowerCase()}. They need an account already; ask them to sign up first.</span>
        </form>
      )}
      {error && <p className="form-error" role="alert">{error}</p>}

      <div className="card table-card">
        {members.state === "failed" && !members.data && <Failed message={members.error} onRetry={reload} />}
        <div className="table-wrap">
        <table className="table">
          <thead><tr><th>Member</th><th>Role</th><th className="hide-sm">Joined</th><th aria-label="Actions" /></tr></thead>
          {!members.data ? <SkeletonRows columns={4} rows={3} /> : (
            <tbody>
              {members.data.map((m) => (
                <tr key={m.userId}>
                  <td>
                    <div className="row">
                      <span className="avatar">{initials(m.displayName)}</span>
                      <span className="grow"><span className="block">{m.displayName}{m.userId === session.user.id && <span className="muted"> (you)</span>}</span><span className="small muted">{m.email}</span></span>
                    </div>
                  </td>
                  <td>
                    {owner ? (
                      <select className="select narrow" value={m.role} aria-label={`Role of ${m.displayName}`}
                        onChange={(e) => act(() => api.changeRole(workspace.id, m.userId, e.target.value as Role))}>
                        {roles.map((r) => <option key={r}>{r}</option>)}
                      </select>
                    ) : <RoleBadge role={m.role} />}
                  </td>
                  <td className="hide-sm">{relative(m.joinedAt)}</td>
                  <td className="actions">
                    {owner && m.userId !== session.user.id && (
                      <button className="icon-button" aria-label={`Remove ${m.displayName}`} title="Remove" onClick={() => act(() => api.removeMember(workspace.id, m.userId))}><Trash2 size={15} /></button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          )}
        </table>
        </div>
      </div>

      {owner && <WorkspaceSettings />}
    </>
  );
}
