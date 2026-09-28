import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { routes } from '../../app/app.routes';
import { RUNTIME_CONFIG } from '../../shared/api/runtime-config';
import { authInterceptor } from '../../shared/auth/auth.interceptor';
import { SessionStore } from './session.store';

describe('session return and sign out', () => {
  beforeEach(() => {
    sessionStorage.clear();
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideRouter(routes),
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        {
          provide: RUNTIME_CONFIG,
          useValue: { webApiBaseUrl: 'http://api.test', identityAuthority: 'http://identity.test' },
        },
      ],
    });
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue({
        ok: true,
        json: async () => ({
          authorization_endpoint: 'https://identity.test/connect/authorize',
          token_endpoint: 'https://identity.test/connect/token',
        }),
      }),
    );
    vi.stubGlobal('location', {
      origin: 'http://localhost:4200',
      href: 'http://localhost:4200/',
      pathname: '/',
      search: '',
      assign: vi.fn(),
    });
  });

  afterEach(() => {
    const http = TestBed.inject(HttpTestingController);
    for (const request of http.match((candidate) => candidate.url.includes('/users/'))) {
      request.flush({ items: [], page: 1, pageSize: 20, totalCount: 0, id: 'cust-1', name: 'Jordan' });
    }
    http.verify();
    vi.unstubAllGlobals();
  });

  it('starts authorize instead of asking for a pasted token', async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      json: async () => ({
        authorization_endpoint: 'https://identity.test/connect/authorize',
        token_endpoint: 'https://identity.test/connect/token',
      }),
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

    const harness = await RouterTestingHarness.create('/customers/cust-1');
    const element = harness.fixture.nativeElement as HTMLElement;

    expect(TestBed.inject(Router).url).toBe('/session');
    expect(TestBed.inject(SessionStore).returnTo()).toBe('/customers/cust-1');
    expect(element.textContent).toContain('Redirecting to sign in');
    expect(element.querySelector('#access-token')).toBeNull();
    expect(element.querySelector('#refresh-token')).toBeNull();
    expect(element.querySelector('input[type="password"]')).toBeNull();
    await vi.waitUntil(() => assign.mock.calls.length === 1);
    const redirected = new URL(assign.mock.calls[0][0] as string);
    expect(redirected.searchParams.get('client_id')).toBe('adviser-portal-angular');

    await harness.navigateByUrl('/login');
    expect(harness.fixture.nativeElement.textContent).toContain('Redirecting to sign in');
  });

  it('sends login to the session page and keeps an invalid callback on its own url', async () => {
    const harness = await RouterTestingHarness.create('/login');
    expect(TestBed.inject(Router).url).toBe('/session');
    await harness.navigateByUrl('/callback');
    expect(TestBed.inject(Router).url).toBe('/callback');
    await vi.waitUntil(() => {
      harness.detectChanges();
      return (harness.fixture.nativeElement.textContent as string).includes('Sign-in callback was invalid.');
    });
  });

  it('sign out clears tokens and returns to the sign-in redirect', async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      json: async () => ({ authorization_endpoint: 'https://identity.test/connect/authorize' }),
    });
    vi.stubGlobal('fetch', fetchMock);
    const assign = vi.fn();
    vi.stubGlobal('location', {
      origin: 'http://localhost:4200',
      href: 'http://localhost:4200/customers',
      pathname: '/customers',
      search: '',
      assign,
    });
    const session = TestBed.inject(SessionStore);
    session.setTokens('access-1', 'refresh-1');
    session.setCurrentUser({
      id: 'user-1',
      name: 'Ada',
      email: 'ada@firm',
      role: 'tenantAdmin',
      status: 'active',
      tenantId: 'tenant-1',
      tenantCode: 'firm',
      adviserId: null,
      rowVersion: 'rv',
    });
    const harness = await RouterTestingHarness.create('/customers');
    const button = [...harness.fixture.nativeElement.querySelectorAll('button')].find((item) =>
      item.textContent?.includes('Sign out'),
    ) as HTMLButtonElement;

    button.click();
    await harness.fixture.whenStable();
    await vi.waitUntil(() => assign.mock.calls.length === 1);

    expect(session.accessToken()).toBeNull();
    expect(session.refreshToken()).toBeNull();
    expect(TestBed.inject(Router).url).toBe('/session');
    expect(fetchMock).toHaveBeenCalledWith('http://identity.test/.well-known/openid-configuration');
  });
});
