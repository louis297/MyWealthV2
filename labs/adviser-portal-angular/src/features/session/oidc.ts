import { challengeS256, randomUrlSafe } from './pkce';
import { isInAppPath } from './session.paths';
import {
  PKCE_PENDING_KEY,
  PKCE_STATE_KEY,
  PKCE_VERIFIER_KEY,
  RETURN_TO_KEY,
  SIGN_OUT_IN_PROGRESS_KEY,
} from './session.storage';

export const CLIENT_ID = 'adviser-portal-angular';
export const SCOPE = 'openid profile offline_access api';

type DiscoveryDocument = {
  authorization_endpoint: string;
  token_endpoint: string;
  revocation_endpoint?: string;
  end_session_endpoint?: string;
};

sessionStorage.removeItem(SIGN_OUT_IN_PROGRESS_KEY);

export function isSignOutInProgress(): boolean {
  return sessionStorage.getItem(SIGN_OUT_IN_PROGRESS_KEY) === '1';
}

export function redirectUri(): string {
  return `${window.location.origin}/callback`;
}

export async function discover(authority: string): Promise<DiscoveryDocument> {
  const response = await fetch(`${authority.replace(/\/$/, '')}/.well-known/openid-configuration`);
  if (!response.ok) {
    throw new Error('Identity discovery failed.');
  }

  return (await response.json()) as DiscoveryDocument;
}

export function buildAuthorizeUrl(options: {
  authorizationEndpoint: string;
  redirectUri: string;
  state: string;
  challenge: string;
}): string {
  const url = new URL(options.authorizationEndpoint);
  url.searchParams.set('client_id', CLIENT_ID);
  url.searchParams.set('redirect_uri', options.redirectUri);
  url.searchParams.set('response_type', 'code');
  url.searchParams.set('scope', SCOPE);
  url.searchParams.set('code_challenge', options.challenge);
  url.searchParams.set('code_challenge_method', 'S256');
  url.searchParams.set('state', options.state);
  return url.toString();
}

export async function startAuthorize(authority: string): Promise<void> {
  if (isSignOutInProgress()) {
    return;
  }

  if (sessionStorage.getItem(PKCE_PENDING_KEY) === '1') {
    return;
  }

  sessionStorage.setItem(PKCE_PENDING_KEY, '1');
  try {
    const pathname = window.location.pathname || '/';
    const search = window.location.search || '';
    const returnTo = `${pathname}${search}`;
    if (isInAppPath(returnTo) && pathname !== '/callback') {
      sessionStorage.setItem(RETURN_TO_KEY, returnTo);
    }

    const verifier = randomUrlSafe();
    const state = randomUrlSafe(16);
    const challenge = await challengeS256(verifier);
    sessionStorage.setItem(PKCE_VERIFIER_KEY, verifier);
    sessionStorage.setItem(PKCE_STATE_KEY, state);

    const { authorization_endpoint } = await discover(authority);
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

let callbackFlight: Promise<string> | null = null;

export function completeCallback(
  session: { setTokens(accessToken: string, refreshToken: string | null): void },
  authority = '',
): Promise<string> {
  if (!callbackFlight) {
    callbackFlight = redeemCallback(session, authority).finally(() => {
      callbackFlight = null;
    });
  }

  return callbackFlight;
}

async function redeemCallback(
  session: { setTokens(accessToken: string, refreshToken: string | null): void },
  authority: string,
): Promise<string> {
  const params = new URLSearchParams(window.location.search);
  const code = params.get('code');
  const state = params.get('state');
  const expectedState = sessionStorage.getItem(PKCE_STATE_KEY);
  const verifier = sessionStorage.getItem(PKCE_VERIFIER_KEY);
  const returnTo = sessionStorage.getItem(RETURN_TO_KEY);

  if (!code || !state || !verifier || state !== expectedState) {
    throw new Error('Sign-in callback was invalid.');
  }

  const current = new URL(window.location.href);
  current.searchParams.delete('code');
  window.history.replaceState(null, '', `${current.pathname}${current.search}${current.hash}`);

  const { token_endpoint } = await discover(authority);
  const response = await fetch(token_endpoint, {
    method: 'POST',
    headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
    body: new URLSearchParams({
      grant_type: 'authorization_code',
      code,
      code_verifier: verifier,
      redirect_uri: redirectUri(),
      client_id: CLIENT_ID,
    }),
  });

  if (!response.ok) {
    throw new Error('Token exchange failed.');
  }

  const tokens = (await response.json()) as { access_token?: string; refresh_token?: string };
  if (!tokens.access_token) {
    throw new Error('Token exchange failed.');
  }

  session.setTokens(tokens.access_token, tokens.refresh_token ?? null);
  sessionStorage.removeItem(PKCE_VERIFIER_KEY);
  sessionStorage.removeItem(PKCE_STATE_KEY);
  sessionStorage.removeItem(PKCE_PENDING_KEY);
  sessionStorage.removeItem(RETURN_TO_KEY);

  return isInAppPath(returnTo) ? returnTo : '/';
}
