import { challengeS256 } from './pkce';
import { startAuthorize } from './oidc';
import { PKCE_STATE_KEY, PKCE_VERIFIER_KEY, RETURN_TO_KEY } from './session.storage';

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
});
