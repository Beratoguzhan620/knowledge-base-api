import { BookOpen, Plus } from "lucide-react";
import { useCallback, useEffect, useMemo, useState } from "react";
import { api, setToken } from "./api";
import { NewWorkspaceDialog, Shell } from "./components/Shell";
import { Empty, Failed, Loading } from "./components/States";
import { SessionCtx, WorkspaceCtx, type SessionContext } from "./context";
import { Ask } from "./pages/Ask";
import { Register, SignIn } from "./pages/Auth";
import { DocumentPage } from "./pages/DocumentPage";
import { Documents } from "./pages/Documents";
import { Members } from "./pages/Members";
import { Search } from "./pages/Search";
import { isPublic, linkProps, navigate, useRoute, workspaceOf, type Route } from "./router";
import { loadSession, saveSession } from "./session";
import type { Me, Session } from "./types";

function WorkspacePage({ route }: { route: Route }) {
  switch (route.page) {
    case "ask": return <Ask key={route.workspaceId} conversationId={route.conversationId} />;
    case "documents": return <Documents key={route.workspaceId} />;
    case "document": return <DocumentPage documentId={route.documentId} />;
    case "search": return <Search key={route.workspaceId} initial={route.q} />;
    case "members": return <Members key={route.workspaceId} />;
    default: return null;
  }
}

function NoWorkspace() {
  const [creating, setCreating] = useState(false);
  return (
    <div className="center">
      <div className="card auth-card">
        <Empty title="You are not in any workspace" icon={<BookOpen size={26} />}>
          <span className="muted">Create one for your documents, or ask a workspace owner to add you.</span>
          <button className="button primary" onClick={() => setCreating(true)}><Plus size={16} />New workspace</button>
        </Empty>
      </div>
      {creating && <NewWorkspaceDialog onClose={() => setCreating(false)} />}
    </div>
  );
}

function NotFound() {
  return (
    <div className="center">
      <div className="card auth-card">
        <Empty title="This page does not exist">
          <span className="muted">Or the workspace is not one of yours.</span>
          <a className="button" {...linkProps("/")}>Go to your workspaces</a>
        </Empty>
      </div>
    </div>
  );
}

export function App() {
  const route = useRoute();
  const [session, setSession] = useState<Session | null>(() => loadSession());
  const [me, setMe] = useState<Me | null>(null);
  const [error, setError] = useState<string | null>(null);

  const signOut = useCallback(() => {
    saveSession(null);
    setToken(null, () => {});
    setSession(null);
    setMe(null);
    navigate("/signin", true);
  }, []);

  if (session) setToken(session.accessToken, signOut);

  const refresh = useCallback(async () => {
    const value = await api.me();
    setMe(value);
    return value;
  }, []);

  useEffect(() => {
    if (!session) return;
    setError(null);
    refresh().catch((e: Error) => setError(e.message));
  }, [session, refresh]);

  // A workspace in the address that is not among ours yet may be one we were just added to.
  const workspaceId = workspaceOf(route);
  const workspace = me?.workspaces.find((w) => w.id === workspaceId) ?? null;
  useEffect(() => {
    if (me && workspaceId && !workspace) void refresh();
  }, [workspaceId]); // eslint-disable-line react-hooks/exhaustive-deps

  useEffect(() => {
    if (!session && !isPublic(route)) navigate("/signin", true);
    if (session && isPublic(route)) navigate("/", true);
    if (session && me && route.page === "home" && me.workspaces[0]) navigate(`/w/${me.workspaces[0].id}`, true);
  }, [session, me, route]);

  const context = useMemo<SessionContext | null>(() => (session && me ? { session, me, refresh, signOut } : null), [session, me, refresh, signOut]);

  function signedIn(value: Session) {
    saveSession(value);
    setToken(value.accessToken, signOut);
    setSession(value);
  }

  if (!session) {
    return route.page === "register" ? <Register onSignedIn={signedIn} /> : <SignIn onSignedIn={signedIn} />;
  }

  if (error && !me) return <div className="center"><Failed message={error} onRetry={() => refresh().catch((e: Error) => setError(e.message))} /></div>;
  if (!context) return <div className="center"><Loading /></div>;

  return (
    <SessionCtx.Provider value={context}>
      {route.page === "home" && context.me.workspaces.length === 0 ? <NoWorkspace />
        : workspace ? (
          <WorkspaceCtx.Provider value={workspace}>
            <Shell route={route}><WorkspacePage route={route} /></Shell>
          </WorkspaceCtx.Provider>
        )
        : route.page === "home" ? <div className="center"><Loading /></div>
        : <NotFound />}
    </SessionCtx.Provider>
  );
}
