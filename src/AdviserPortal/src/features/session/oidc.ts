import { challengeS256, randomUrlSafe } from "@/features/session/pkce";
import {
  PKCE_PENDING_KEY,
  PKCE_STATE_KEY,
  PKCE_VERIFIER_KEY,
} from "@/features/session/oidcStorage";
import { clearSession, setTokens } from "@/features/session/sessionSlice";

export const CLIENT_ID = "adviser-portal";
export const SCOPE = "openid profile offline_access api";

type DiscoveryDocument = {
  authorization_endpoint: string;
  token_endpoint: string;
  end_session_endpoint?: string;
};

export function redirectUri(): string {
  return `${window.location.origin}/callback`;
}

function authority(): string {
  return import.meta.env.VITE_IDENTITY_AUTHORITY.replace(/\/$/, "");
}

export function buildAuthorizeUrl(options: {
  authorizationEndpoint: string;
  redirectUri: string;
  state: string;
  challenge: string;
}): string {
  const url = new URL(options.authorizationEndpoint);
  url.searchParams.set("client_id", CLIENT_ID);
  url.searchParams.set("redirect_uri", options.redirectUri);
  url.searchParams.set("response_type", "code");
  url.searchParams.set("scope", SCOPE);
  url.searchParams.set("code_challenge", options.challenge);
  url.searchParams.set("code_challenge_method", "S256");
  url.searchParams.set("state", options.state);
  return url.toString();
}

export async function discover(): Promise<DiscoveryDocument> {
  const response = await fetch(`${authority()}/.well-known/openid-configuration`);
  if (!response.ok) {
    throw new Error("Identity discovery failed.");
  }

  return (await response.json()) as DiscoveryDocument;
}

export async function startAuthorize(): Promise<void> {
  if (sessionStorage.getItem(PKCE_PENDING_KEY) === "1") {
    return;
  }

  sessionStorage.setItem(PKCE_PENDING_KEY, "1");
  try {
    const verifier = randomUrlSafe();
    const state = randomUrlSafe(16);
    const challenge = await challengeS256(verifier);
    sessionStorage.setItem(PKCE_VERIFIER_KEY, verifier);
    sessionStorage.setItem(PKCE_STATE_KEY, state);

    const { authorization_endpoint } = await discover();
    window.location.assign(
      buildAuthorizeUrl({
        authorizationEndpoint: authorization_endpoint,
        redirectUri: redirectUri(),
        state,
        challenge,
      }),
    );
  } catch (error) {
    sessionStorage.removeItem(PKCE_PENDING_KEY);
    throw error;
  }
}

export async function completeCallback(): Promise<void> {
  const params = new URLSearchParams(window.location.search);
  const code = params.get("code");
  const state = params.get("state");
  const expectedState = sessionStorage.getItem(PKCE_STATE_KEY);
  const verifier = sessionStorage.getItem(PKCE_VERIFIER_KEY);

  if (!code || !state || !verifier || state !== expectedState) {
    throw new Error("Sign-in callback was invalid.");
  }

  const { token_endpoint } = await discover();
  const body = new URLSearchParams({
    grant_type: "authorization_code",
    code,
    code_verifier: verifier,
    redirect_uri: redirectUri(),
    client_id: CLIENT_ID,
  });

  const response = await fetch(token_endpoint, {
    method: "POST",
    headers: { "Content-Type": "application/x-www-form-urlencoded" },
    body,
  });

  if (!response.ok) {
    throw new Error("Token exchange failed.");
  }

  const tokens = (await response.json()) as {
    access_token: string;
    refresh_token?: string;
  };

  const { store } = await import("@/app/store");
  store.dispatch(
    setTokens({
      accessToken: tokens.access_token,
      refreshToken: tokens.refresh_token ?? null,
    }),
  );
  sessionStorage.removeItem(PKCE_VERIFIER_KEY);
  sessionStorage.removeItem(PKCE_STATE_KEY);
  sessionStorage.removeItem(PKCE_PENDING_KEY);
}

export async function startEndSession(): Promise<void> {
  const { store } = await import("@/app/store");
  store.dispatch(clearSession());

  const { end_session_endpoint } = await discover();
  if (!end_session_endpoint) {
    return;
  }

  const url = new URL(end_session_endpoint);
  url.searchParams.set("client_id", CLIENT_ID);
  url.searchParams.set("post_logout_redirect_uri", `${window.location.origin}/`);
  window.location.assign(url.toString());
}

export async function refreshTokens(
  refreshToken: string,
): Promise<{ accessToken: string; refreshToken: string | null } | null> {
  const { token_endpoint } = await discover();
  const response = await fetch(token_endpoint, {
    method: "POST",
    headers: { "Content-Type": "application/x-www-form-urlencoded" },
    body: new URLSearchParams({
      grant_type: "refresh_token",
      refresh_token: refreshToken,
      client_id: CLIENT_ID,
    }),
  });

  if (!response.ok) {
    return null;
  }

  const tokens = (await response.json()) as {
    access_token: string;
    refresh_token?: string;
  };

  return {
    accessToken: tokens.access_token,
    refreshToken: tokens.refresh_token ?? null,
  };
}
