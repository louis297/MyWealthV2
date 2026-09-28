import { challengeS256 } from './pkce';
import { completeCallback, startAuthorize } from './oidc';
import {
  ACCESS_TOKEN_KEY,
  PKCE_STATE_KEY,
  PKCE_VERIFIER_KEY,
  REFRESH_TOKEN_KEY,
  RETURN_TO_KEY,
} from './session.storage';

const discovery = {
  authorization_endpoint: 'https://identity.test/connect/authorize',
  token_endpoint: 'https://identity.test/connect/token',
};

describe('authorize', () => {
  beforeEach(() => {
    sessionStorage.clear();
    vi.unstubAllGlobals();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('redirects to the discovered authorize endpoint with PKCE for this client', async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      json: async () => discovery,
    });
    vi.stubGlobal('fetch', fetchMock);
    const assign = vi.fn();
    vi.stubGlobal('location', {
      origin: 'http://localhost:4200',
      href: 'http://localhost:4200/customers/cust-1',
      pathname: '/customers/cust-1',
      search: '',
      assign,
    });

    await startAuthorize('https://identity.test');

    expect(fetchMock).toHaveBeenCalledWith('https://identity.test/.well-known/openid-configuration');
    expect(sessionStorage.getItem(PKCE_VERIFIER_KEY)).toBeTruthy();
    expect(sessionStorage.getItem(PKCE_STATE_KEY)).toBeTruthy();
    expect(sessionStorage.getItem(RETURN_TO_KEY)).toBe('/customers/cust-1');
    expect(assign).toHaveBeenCalledOnce();
    const redirected = new URL(assign.mock.calls[0][0] as string);
    expect(redirected.origin + redirected.pathname).toBe(discovery.authorization_endpoint);
    expect(redirected.searchParams.get('client_id')).toBe('adviser-portal-angular');
    expect(redirected.searchParams.get('redirect_uri')).toBe('http://localhost:4200/callback');
    expect(redirected.searchParams.get('response_type')).toBe('code');
    expect(redirected.searchParams.get('scope')).toBe('openid profile offline_access api');
    expect(redirected.searchParams.get('code_challenge_method')).toBe('S256');
    expect(redirected.searchParams.get('code_challenge')).toBe(
      await challengeS256(sessionStorage.getItem(PKCE_VERIFIER_KEY) ?? ''),
    );
    expect(redirected.searchParams.get('state')).toBe(sessionStorage.getItem(PKCE_STATE_KEY));

    await startAuthorize('https://identity.test');
    expect(assign).toHaveBeenCalledOnce();
  });

  it('does not store a return path outside the app', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: true, json: async () => discovery }));
    vi.stubGlobal('location', {
      origin: 'http://localhost:4200',
      href: 'http://localhost:4200/not-a-page',
      pathname: '/not-a-page',
      search: '',
      assign: vi.fn(),
    });

    await startAuthorize('https://identity.test');

    expect(sessionStorage.getItem(RETURN_TO_KEY)).toBeNull();
  });

  it('exchanges the callback code and stores the tokens', async () => {
    sessionStorage.setItem(PKCE_VERIFIER_KEY, 'verifier-1');
    sessionStorage.setItem(PKCE_STATE_KEY, 'state-1');
    sessionStorage.setItem(RETURN_TO_KEY, '/customers/cust-1');
    const fetchMock = vi.fn()
      .mockResolvedValueOnce({ ok: true, json: async () => discovery })
      .mockResolvedValueOnce({
        ok: true,
        json: async () => ({ access_token: 'access-token', refresh_token: 'refresh-token' }),
      });
    vi.stubGlobal('fetch', fetchMock);
    const locationUrl = new URL('http://localhost:4200/callback?code=abc&state=state-1');
    stubCallbackLocation(locationUrl);

    const path = await completeCallback(tokenSession());

    expect(path).toBe('/customers/cust-1');
    expect(sessionStorage.getItem(ACCESS_TOKEN_KEY)).toBe('access-token');
    expect(sessionStorage.getItem(REFRESH_TOKEN_KEY)).toBe('refresh-token');
    expect(sessionStorage.getItem(PKCE_VERIFIER_KEY)).toBeNull();
    expect(sessionStorage.getItem(PKCE_STATE_KEY)).toBeNull();
    expect(sessionStorage.getItem(RETURN_TO_KEY)).toBeNull();
    expect(fetchMock.mock.calls[1][0]).toBe(discovery.token_endpoint);
    const body = new URLSearchParams(fetchMock.mock.calls[1][1].body as string);
    expect(body.get('grant_type')).toBe('authorization_code');
    expect(body.get('code')).toBe('abc');
    expect(body.get('code_verifier')).toBe('verifier-1');
    expect(body.get('redirect_uri')).toBe('http://localhost:4200/callback');
    expect(body.get('client_id')).toBe('adviser-portal-angular');
    expect(locationUrl.searchParams.get('code')).toBeNull();
  });

  it('exchanges one authorization code once when completion overlaps', async () => {
    sessionStorage.setItem(PKCE_VERIFIER_KEY, 'verifier-1');
    sessionStorage.setItem(PKCE_STATE_KEY, 'state-1');
    let releaseToken: () => void = () => undefined;
    const tokenGate = new Promise<void>((resolve) => {
      releaseToken = resolve;
    });
    const fetchMock = vi.fn().mockImplementation(async (input: string) => {
      if (String(input).includes('openid-configuration')) {
        return { ok: true, json: async () => discovery };
      }
      await tokenGate;
      return {
        ok: true,
        json: async () => ({ access_token: 'access-once', refresh_token: 'refresh-once' }),
      };
    });
    vi.stubGlobal('fetch', fetchMock);
    stubCallbackLocation(new URL('http://localhost:4200/callback?code=once-code&state=state-1'));

    const first = completeCallback(tokenSession());
    const second = completeCallback(tokenSession());
    releaseToken();
    await Promise.all([first, second]);

    const tokenCalls = fetchMock.mock.calls.filter(([input]) => input === discovery.token_endpoint);
    expect(tokenCalls).toHaveLength(1);
  });

  it('rejects a callback whose state does not match and stores nothing', async () => {
    sessionStorage.setItem(PKCE_VERIFIER_KEY, 'verifier-1');
    sessionStorage.setItem(PKCE_STATE_KEY, 'state-1');
    vi.stubGlobal('fetch', vi.fn());
    stubCallbackLocation(new URL('http://localhost:4200/callback?code=abc&state=nope'));

    await expect(completeCallback(tokenSession())).rejects.toThrow('Sign-in callback was invalid.');
    expect(sessionStorage.getItem(ACCESS_TOKEN_KEY)).toBeNull();
  });

  it('ignores a stored return path outside the app', async () => {
    sessionStorage.setItem(PKCE_VERIFIER_KEY, 'verifier-1');
    sessionStorage.setItem(PKCE_STATE_KEY, 'state-1');
    sessionStorage.setItem(RETURN_TO_KEY, 'https://evil.example/');
    vi.stubGlobal('fetch', vi.fn()
      .mockResolvedValueOnce({ ok: true, json: async () => discovery })
      .mockResolvedValueOnce({
        ok: true,
        json: async () => ({ access_token: 'access-token', refresh_token: 'refresh-token' }),
      }));
    stubCallbackLocation(new URL('http://localhost:4200/callback?code=abc&state=state-1'));

    await expect(completeCallback(tokenSession())).resolves.toBe('/');
  });
});

function tokenSession() {
  return {
    setTokens(accessToken: string, refreshToken: string | null) {
      sessionStorage.setItem(ACCESS_TOKEN_KEY, accessToken);
      if (refreshToken) {
        sessionStorage.setItem(REFRESH_TOKEN_KEY, refreshToken);
      } else {
        sessionStorage.removeItem(REFRESH_TOKEN_KEY);
      }
    },
  };
}

function stubCallbackLocation(locationUrl: URL): void {
  vi.stubGlobal('location', {
    get origin() {
      return locationUrl.origin;
    },
    get href() {
      return locationUrl.href;
    },
    get search() {
      return locationUrl.search;
    },
    get pathname() {
      return locationUrl.pathname;
    },
    assign: vi.fn(),
  });
  vi.spyOn(window.history, 'replaceState').mockImplementation((_state, _title, url) => {
    locationUrl.href = new URL(String(url), locationUrl).href;
  });
}
