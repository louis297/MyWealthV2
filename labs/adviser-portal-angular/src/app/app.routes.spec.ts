import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { authInterceptor } from '../shared/auth/auth.interceptor';
import { RUNTIME_CONFIG } from '../shared/api/runtime-config';
import { routes } from './app.routes';
import { SessionStore } from '../features/session/session.store';
import { CurrentUser } from '../features/session/session.models';

describe('role shell', () => {
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
        json: async () => ({ authorization_endpoint: 'https://identity.test/connect/authorize' }),
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
    flushPeople();
    TestBed.inject(HttpTestingController).verify();
    vi.unstubAllGlobals();
  });

  it('sends a signed-out visit to /customers to the session page', async () => {
    const harness = await RouterTestingHarness.create('/customers');
    expect(TestBed.inject(Router).url).toBe('/session');
    expect(harness.fixture.nativeElement.textContent).toContain('Redirecting to sign in');
    expect(harness.fixture.nativeElement.querySelector('#access-token')).toBeNull();
  });

  it('shows the firm name and staff navigation for a tenant admin', async () => {
    signIn(tenantAdmin(), 'Northwind');
    const harness = await RouterTestingHarness.create('/');
    const text = harness.fixture.nativeElement.textContent as string;

    expect(TestBed.inject(Router).url).toBe('/customers');
    expect(text).toContain('Northwind');
    expect(linkText(harness)).toEqual(['Customers', 'Advisers', 'Profile']);
  });

  it('hides advisers from an adviser and forbids those routes', async () => {
    signIn(adviser());
    const harness = await RouterTestingHarness.create('/');
    expect(linkText(harness)).toEqual(['Customers', 'Profile']);

    await harness.navigateByUrl('/advisers');
    expect(forbiddenText(harness)).toContain('You do not have access to this page.');
    await harness.navigateByUrl('/advisers/new');
    expect(forbiddenText(harness)).toContain('You do not have access to this page.');
  });

  it('gives a system admin profile without a customers workspace', async () => {
    signIn(systemAdmin());
    const harness = await RouterTestingHarness.create('/');
    const root = harness.fixture.nativeElement as HTMLElement;

    expect(TestBed.inject(Router).url).toBe('/profile');
    expect(root.querySelector('h1')?.textContent).toContain('Profile');
    expect(linkText(harness)).toEqual(['Profile']);
    expect(root.textContent).not.toContain('Customers');

    await harness.navigateByUrl('/customers');
    expect(forbiddenText(harness)).toContain('You do not have access to this page.');
    await harness.navigateByUrl('/session');
    expect(TestBed.inject(Router).url).toBe('/session');
  });

  it('loads the user and firm name from a stored token', async () => {
    const session = TestBed.inject(SessionStore);
    session.setTokens('access-1', null);
    const harness = await RouterTestingHarness.create('/customers');
    const http = TestBed.inject(HttpTestingController);

    const me = http.expectOne('http://api.test/users/me');
    expect(me.request.headers.get('Authorization')).toBe('Bearer access-1');
    me.flush(tenantAdmin());
    http.expectOne('http://api.test/tenants/by-code/firm').flush({ id: 'tenant-1', name: 'Northwind', code: 'firm' });
    harness.detectChanges();

    expect(session.currentUser()?.name).toBe('Ada');
    expect(session.tenantName()).toBe('Northwind');
    expect(harness.fixture.nativeElement.textContent).toContain('Northwind');

    await harness.navigateByUrl('/profile');
    http.expectNone('http://api.test/users/me');
  });

  it('sends a stored customer token to forbidden after the probe', async () => {
    TestBed.inject(SessionStore).setTokens('access-1', null);
    const harness = await RouterTestingHarness.create('/customers');
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('http://api.test/users/me').flush(customer());
    await harness.fixture.whenStable();
    expect(TestBed.inject(Router).url).toBe('/forbidden');
  });

  it('sends a customer token to the forbidden page', async () => {
    signIn(customer());
    const harness = await RouterTestingHarness.create('/customers');
    const text = forbiddenText(harness);
    expect(text).toContain('You do not have access to this page.');
    expect(text).toContain('A token is present, but this page is not allowed.');
  });
});

function flushPeople(): void {
  const http = TestBed.inject(HttpTestingController);
  for (const request of http.match((candidate) => candidate.url.includes('/users/'))) {
    request.flush({ items: [], page: 1, pageSize: 20, totalCount: 0 });
  }
}

function signIn(user: CurrentUser, tenantName: string | null = null): void {
  const session = TestBed.inject(SessionStore);
  session.setTokens('access-1', null);
  session.setCurrentUser(user);
  session.setTenantName(tenantName);
}

function linkText(harness: RouterTestingHarness): string[] {
  return [...harness.fixture.nativeElement.querySelectorAll('nav a')].map((link) =>
    (link.textContent ?? '').trim(),
  );
}

function forbiddenText(harness: RouterTestingHarness): string {
  return harness.fixture.nativeElement.textContent as string;
}

function user(role: string, extra: Partial<CurrentUser> = {}): CurrentUser {
  return {
    id: 'user-1',
    name: 'Ada',
    email: 'ada@firm',
    role,
    status: 'active',
    tenantId: role === 'systemAdmin' ? null : 'tenant-1',
    tenantCode: role === 'systemAdmin' ? null : 'firm',
    adviserId: role === 'adviser' ? 'user-1' : null,
    rowVersion: 'rv',
    ...extra,
  };
}

function tenantAdmin(): CurrentUser {
  return user('tenantAdmin');
}

function adviser(): CurrentUser {
  return user('adviser', { id: 'adv-1', adviserId: 'adv-1' });
}

function systemAdmin(): CurrentUser {
  return user('systemAdmin', { name: 'Sys' });
}

function customer(): CurrentUser {
  return user('customer', { adviserId: 'adv-1' });
}
