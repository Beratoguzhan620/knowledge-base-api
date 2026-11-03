import { useEffect, useState } from "react";

/** The pages of the app. Everything inside a workspace carries its id in the address, so links can be shared with members. */
export type Route =
  | { page: "signin" }
  | { page: "register" }
  | { page: "home" }
  | { page: "ask"; workspaceId: string; conversationId?: string }
  | { page: "documents"; workspaceId: string }
  | { page: "document"; workspaceId: string; documentId: string }
  | { page: "search"; workspaceId: string; q?: string }
  | { page: "members"; workspaceId: string }
  | { page: "not-found" };

const guid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export function parseRoute(pathname: string, search = ""): Route {
  const parts = pathname.split("/").filter(Boolean).map(decodeURIComponent);
  const [first, workspaceId, section, id] = parts;

  if (parts.length === 0) return { page: "home" };
  if (parts.length === 1 && first === "signin") return { page: "signin" };
  if (parts.length === 1 && first === "register") return { page: "register" };
  if (first !== "w" || !workspaceId || !guid.test(workspaceId)) return { page: "not-found" };
  if (parts.length === 2) return { page: "ask", workspaceId };
  if (section === "c" && parts.length === 4 && guid.test(id)) return { page: "ask", workspaceId, conversationId: id };
  if (section === "documents" && parts.length === 3) return { page: "documents", workspaceId };
  if (section === "documents" && parts.length === 4 && guid.test(id)) return { page: "document", workspaceId, documentId: id };
  if (section === "search" && parts.length === 3) return { page: "search", workspaceId, q: new URLSearchParams(search).get("q") ?? undefined };
  if (section === "members" && parts.length === 3) return { page: "members", workspaceId };
  return { page: "not-found" };
}

export const isPublic = (route: Route) => route.page === "signin" || route.page === "register";

export const workspaceOf = (route: Route): string | undefined => ("workspaceId" in route ? route.workspaceId : undefined);

export function navigate(path: string, replace = false) {
  if (replace) history.replaceState(null, "", path);
  else history.pushState(null, "", path);
  window.dispatchEvent(new PopStateEvent("popstate"));
}

export function useRoute(): Route {
  const read = () => parseRoute(location.pathname, location.search);
  const [route, setRoute] = useState<Route>(read);
  useEffect(() => {
    const update = () => setRoute(read());
    window.addEventListener("popstate", update);
    return () => window.removeEventListener("popstate", update);
  }, []);
  return route;
}

/** Props for an anchor that moves within the app without reloading the page. */
export function linkProps(href: string) {
  return {
    href,
    onClick: (e: React.MouseEvent) => {
      if (e.metaKey || e.ctrlKey || e.shiftKey || e.button !== 0) return;
      e.preventDefault();
      navigate(href);
    },
  };
}
