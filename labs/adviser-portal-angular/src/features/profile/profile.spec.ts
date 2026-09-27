import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { routes } from '../../app/app.routes';
import { RUNTIME_CONFIG } from '../../shared/api/runtime-config';
import { authInterceptor } from '../../shared/auth/auth.interceptor';
import { SessionStore } from '../session/session.store';

describe('profile', () => {
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
    TestBed.inject(HttpTestingController).verify();
  });

  it('updates the name and clears the session after a password change', async () => {
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
      rowVersion: 'rv-1',
    });
    const harness = await RouterTestingHarness.create('/profile');
    const http = TestBed.inject(HttpTestingController);
    const element = harness.fixture.nativeElement as HTMLElement;
    const name = element.querySelector('#profile-name') as HTMLInputElement;
    name.value = 'Ada Lovelace';
    element.querySelectorAll('form')[0]!.dispatchEvent(new Event('submit'));

    const update = http.expectOne('http://api.test/users/me');
    expect(update.request.method).toBe('PUT');
    expect(update.request.body).toEqual({ name: 'Ada Lovelace', rowVersion: 'rv-1' });
    update.flush(null);

    const current = element.querySelector('#profile-current-password') as HTMLInputElement;
    const next = element.querySelector('#profile-new-password') as HTMLInputElement;
    current.value = 'Old-pass-1';
    next.value = 'New-pass-1';
    element.querySelectorAll('form')[1]!.dispatchEvent(new Event('submit'));

    const password = http.expectOne('http://api.test/users/me/password');
    expect(password.request.method).toBe('PUT');
    expect(password.request.body).toEqual({ currentPassword: 'Old-pass-1', newPassword: 'New-pass-1' });
    password.flush(null, { status: 204, statusText: 'No Content' });
    await harness.fixture.whenStable();

    expect(sessionStorage.length).toBe(0);
    expect(TestBed.inject(Router).url).toBe('/session');
  });
});
