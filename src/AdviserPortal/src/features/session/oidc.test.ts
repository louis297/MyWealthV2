import { beforeEach, describe, expect, it, vi } from "vitest";
import { buildAuthorizeUrl, completeCallback, startAuthorize, startEndSession } from "@/features/session/oidc";
import { store } from "@/app/store";
import { setTokens } from "@/features/session/sessionSlice";
import {
  ACCESS_TOKEN_KEY,
  PKCE_STATE_KEY,
  PKCE_VERIFIER_KEY,
  REFRESH_TOKEN_KEY,
} from "@/features/session/oidcStorage";

const authority = "https://identity.test";
const discovery = {
  authorization_endpoint: "https://identity.test/connect/authorize",
  token_endpoint: "https://identity.test/connect/token",
};

describe("OIDC authorize and callback", () => {
  beforeEach(() => {
    sessionStorage.clear();
    vi.unstubAllGlobals();
    vi.stubEnv("VITE_IDENTITY_AUTHORITY", authority);
    vi.stubGlobal("fetch", vi.fn());
  });

  it("builds the authorize URL from discovery without hard-coded connect paths", () => {
    const url = buildAuthorizeUrl({
      authorizationEndpoint: discovery.authorization_endpoint,
      redirectUri: "http://localhost:5173/callback",
      state: "state-1",
      challenge: "challenge-1",
    });

    const parsed = new URL(url);
    expect(parsed.origin + parsed.pathname).toBe(discovery.authorization_endpoint);
    expect(parsed.searchParams.get("client_id")).toBe("adviser-portal");
    expect(parsed.searchParams.get("redirect_uri")).toBe("http://localhost:5173/callback");
    expect(parsed.searchParams.get("response_type")).toBe("code");
    expect(parsed.searchParams.get("scope")).toBe("openid profile offline_access api");
    expect(parsed.searchParams.get("code_challenge")).toBe("challenge-1");
    expect(parsed.searchParams.get("code_challenge_method")).toBe("S256");
    expect(parsed.searchParams.get("state")).toBe("state-1");
  });

  it("starts authorize from discovery and stores PKCE material", async () => {
    const fetchMock = vi.mocked(fetch);
    fetchMock.mockResolvedValueOnce(
      new Response(JSON.stringify(discovery), {
        status: 200,
        headers: { "Content-Type": "application/json" },
      }),
    );

    const assign = vi.fn();
    vi.stubGlobal("location", {
      origin: "http://localhost:5173",
      href: "http://localhost:5173/",
      assign,
    });

    await startAuthorize();

    expect(fetchMock).toHaveBeenCalledWith(
      "https://identity.test/.well-known/openid-configuration",
    );
    expect(sessionStorage.getItem(PKCE_VERIFIER_KEY)).toBeTruthy();
    expect(sessionStorage.getItem(PKCE_STATE_KEY)).toBeTruthy();
    expect(assign).toHaveBeenCalledOnce();
    const redirected = new URL(assign.mock.calls[0][0] as string);
    expect(redirected.searchParams.get("redirect_uri")).toBe(
      "http://localhost:5173/callback",
    );
  });

  it("exchanges the callback code at the token endpoint and stores tokens", async () => {
    sessionStorage.setItem(PKCE_VERIFIER_KEY, "verifier-1");
    sessionStorage.setItem(PKCE_STATE_KEY, "state-1");

    const fetchMock = vi.mocked(fetch);
    fetchMock
      .mockResolvedValueOnce(
        new Response(JSON.stringify(discovery), {
          status: 200,
          headers: { "Content-Type": "application/json" },
        }),
      )
      .mockResolvedValueOnce(
        new Response(
          JSON.stringify({
            access_token: "access-token",
            refresh_token: "refresh-token",
          }),
          { status: 200, headers: { "Content-Type": "application/json" } },
        ),
      );

    vi.stubGlobal("location", {
      origin: "http://localhost:5173",
      href: "http://localhost:5173/callback?code=abc&state=state-1",
      search: "?code=abc&state=state-1",
    });

    await completeCallback();

    const tokenCall = fetchMock.mock.calls[1];
    expect(tokenCall[0]).toBe(discovery.token_endpoint);
    const body = new URLSearchParams(tokenCall[1]?.body as string);
    expect(body.get("grant_type")).toBe("authorization_code");
    expect(body.get("code")).toBe("abc");
    expect(body.get("code_verifier")).toBe("verifier-1");
    expect(body.get("redirect_uri")).toBe("http://localhost:5173/callback");
    expect(body.get("client_id")).toBe("adviser-portal");

    expect(store.getState().session.accessToken).toBe("access-token");
    expect(store.getState().session.refreshToken).toBe("refresh-token");
  });

  it("clears the portal session and redirects to discovery end-session", async () => {
    store.dispatch(setTokens({ accessToken: "access-1", refreshToken: "refresh-1" }));

    const fetchMock = vi.mocked(fetch);
    fetchMock.mockResolvedValueOnce(
      new Response(
        JSON.stringify({
          ...discovery,
          end_session_endpoint: "https://identity.test/connect/logout",
        }),
        {
          status: 200,
          headers: { "Content-Type": "application/json" },
        },
      ),
    );

    const assign = vi.fn();
    vi.stubGlobal("location", {
      origin: "http://localhost:5173",
      href: "http://localhost:5173/customers",
      assign,
    });

    await startEndSession();

    expect(store.getState().session.accessToken).toBeNull();
    expect(store.getState().session.refreshToken).toBeNull();
    expect(sessionStorage.getItem(ACCESS_TOKEN_KEY)).toBeNull();
    expect(sessionStorage.getItem(REFRESH_TOKEN_KEY)).toBeNull();
    expect(fetchMock).toHaveBeenCalledWith(
      "https://identity.test/.well-known/openid-configuration",
    );
    expect(assign).toHaveBeenCalledOnce();
    const redirected = new URL(assign.mock.calls[0][0] as string);
    expect(redirected.origin + redirected.pathname).toBe("https://identity.test/connect/logout");
    expect(redirected.searchParams.get("client_id")).toBe("adviser-portal");
    expect(redirected.searchParams.get("post_logout_redirect_uri")).toBe(
      "http://localhost:5173/",
    );
  });
});
