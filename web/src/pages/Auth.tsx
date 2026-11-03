import { BookOpen, FileSearch, Quote, ShieldCheck } from "lucide-react";
import { useState, type FormEvent } from "react";
import { api, ApiError } from "../api";
import { linkProps } from "../router";
import type { Session } from "../types";

function Intro() {
  return (
    <aside className="auth-intro">
      <div className="brand"><span className="brand-mark"><BookOpen size={16} /></span>Knowledge Base</div>
      <h2>Answers from your team's own documents</h2>
      <ul>
        <li><FileSearch size={18} /><span>Upload Markdown, text and PDF files; they are split, indexed and searchable in seconds.</span></li>
        <li><Quote size={18} /><span>Every answer cites the passages it comes from, and says so when the documents do not cover a question.</span></li>
        <li><ShieldCheck size={18} /><span>Workspaces keep each team's documents to its members.</span></li>
      </ul>
    </aside>
  );
}

export function SignIn({ onSignedIn }: { onSignedIn: (session: Session) => void }) {
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function submit(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      onSignedIn(await api.login(email, password));
    } catch (err) {
      setError((err as Error).message);
      setBusy(false);
    }
  }

  return (
    <div className="auth">
      <Intro />
      <main className="auth-main">
        <form className="auth-card card" onSubmit={submit}>
          <h1>Sign in</h1>
          <label className="field">Email<input className="input" type="email" autoComplete="email" value={email} onChange={(e) => setEmail(e.target.value)} required autoFocus /></label>
          <label className="field">Password<input className="input" type="password" autoComplete="current-password" value={password} onChange={(e) => setPassword(e.target.value)} required /></label>
          {error && <p className="form-error" role="alert">{error}</p>}
          <button className="button primary wide" disabled={busy}>{busy ? "Signing in…" : "Sign in"}</button>
          <p className="small muted center-text">New here? <a {...linkProps("/register")}>Create an account</a></p>
        </form>
      </main>
    </div>
  );
}

export function Register({ onSignedIn }: { onSignedIn: (session: Session) => void }) {
  const [form, setForm] = useState({ displayName: "", email: "", password: "", workspaceName: "" });
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);
  const set = (key: keyof typeof form) => (e: React.ChangeEvent<HTMLInputElement>) => setForm({ ...form, [key]: e.target.value });
  const fieldError = (name: string) => error?.fields[name]?.[0];

  async function submit(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      onSignedIn(await api.register({ ...form, workspaceName: form.workspaceName.trim() || undefined }));
    } catch (err) {
      setError(err as ApiError);
      setBusy(false);
    }
  }

  return (
    <div className="auth">
      <Intro />
      <main className="auth-main">
        <form className="auth-card card" onSubmit={submit} noValidate>
          <h1>Create an account</h1>
          <label className="field">Your name<input className="input" autoComplete="name" value={form.displayName} onChange={set("displayName")} autoFocus />
            {fieldError("displayName") && <span className="field-error">{fieldError("displayName")}</span>}</label>
          <label className="field">Email<input className="input" type="email" autoComplete="email" value={form.email} onChange={set("email")} />
            {fieldError("email") && <span className="field-error">{fieldError("email")}</span>}</label>
          <label className="field">Password<input className="input" type="password" autoComplete="new-password" value={form.password} onChange={set("password")} />
            <span className="hint">At least 10 characters.</span>
            {fieldError("password") && <span className="field-error">{fieldError("password")}</span>}</label>
          <label className="field">First workspace<input className="input" value={form.workspaceName} onChange={set("workspaceName")} placeholder="My workspace" />
            {fieldError("workspaceName") && <span className="field-error">{fieldError("workspaceName")}</span>}</label>
          {error && Object.keys(error.fields).length === 0 && <p className="form-error" role="alert">{error.message}</p>}
          <button className="button primary wide" disabled={busy}>{busy ? "Creating…" : "Create account"}</button>
          <p className="small muted center-text">Have an account? <a {...linkProps("/signin")}>Sign in</a></p>
        </form>
      </main>
    </div>
  );
}
