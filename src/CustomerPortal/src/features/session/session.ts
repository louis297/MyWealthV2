export const SIGN_OUT_IN_PROGRESS_KEY = "customer-portal.signOutInProgress";

sessionStorage.removeItem(SIGN_OUT_IN_PROGRESS_KEY);

export function isSignOutInProgress(): boolean {
  return sessionStorage.getItem(SIGN_OUT_IN_PROGRESS_KEY) === "1";
}

export function isInAppPath(path: string | null | undefined): path is string {
  if (!path || !path.startsWith("/") || path.startsWith("//") || path.startsWith("/\\") || path.includes("\\")) {
    return false;
  }

  const pathname = path.split("?")[0] ?? "";
  return (
    pathname === "/"
    || pathname === "/dashboard"
    || pathname === "/accounts"
    || pathname.startsWith("/accounts/")
    || pathname === "/reports"
    || pathname === "/profile"
    || pathname === "/forbidden"
  );
}

function configuredBffOrigin(): string | null {
  const configured = import.meta.env.VITE_BFF_HTTP;
  if (typeof configured !== "string" || configured.trim() === "") {
    return null;
  }

  try {
    return new URL(configured).origin;
  } catch {
    return null;
  }
}

export function startLogin(returnUrl?: string): void {
  if (isSignOutInProgress()) {
    return;
  }

  const path = returnUrl ?? `${window.location.pathname || "/"}${window.location.search || ""}`;
  const safe = isInAppPath(path) ? path : "/";
  const bffOrigin = configuredBffOrigin();
  if (bffOrigin && window.location.origin !== bffOrigin) {
    window.location.assign(`${bffOrigin}${safe}`);
    return;
  }

  window.location.assign(`/bff/login?returnUrl=${encodeURIComponent(safe)}`);
}

export async function startEndSession(): Promise<void> {
  sessionStorage.setItem(SIGN_OUT_IN_PROGRESS_KEY, "1");
  await fetch("/bff/logout", {
    method: "POST",
    credentials: "include",
    headers: { "X-MyWealth-Request": "1" },
    redirect: "manual",
  });
  window.location.assign("/bff/logout/continue");
}
