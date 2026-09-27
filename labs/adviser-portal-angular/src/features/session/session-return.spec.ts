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
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('sends callback and login to the session page', async () => {
    const harness = await RouterTestingHarness.create('/callback');
    expect(TestBed.inject(Router).url).toBe('/session');
    await harness.navigateByUrl('/login');
    expect(TestBed.inject(Router).url).toBe('/session');
  });

  it('returns to the signed-out customer link after a successful probe', async () => {
    const harness = await RouterTestingHarness.create('/customers/cust-1');
    expect(TestBed.inject(Router).url).toBe('/session');
    expect(TestBed.inject(SessionStore).returnTo()).toBe('/customers/cust-1');

    const element = harness.fixture.nativeElement as HTMLElement;
    const access = element.querySelector('#access-token') as HTMLTextAreaElement;
    access.value = 'access-1';
    element.querySelector('form')!.dispatchEvent(new Event('submit'));

    TestBed.inject(HttpTestingController)
      .expectOne('http://api.test/users/me')
      .flush({
        id: 'user-1',
        name: 'Ada',
        email: 'ada@firm',
        role: 'tenantAdmin',
        status: 'active',
        tenantId: 'tenant-1',
        tenantCode: null,
        adviserId: null,
        rowVersion: 'rv',
      });
    await harness.fixture.whenStable();

    expect(TestBed.inject(Router).url).toBe('/customers/cust-1');
  });

  it('sign out clears sessionStorage and does not call identity', async () => {
    const fetchMock = vi.fn();
    vi.stubGlobal('fetch', fetchMock);
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

    expect(sessionStorage.length).toBe(0);
    expect(TestBed.inject(Router).url).toBe('/session');
    expect(fetchMock).not.toHaveBeenCalled();
  });
});
