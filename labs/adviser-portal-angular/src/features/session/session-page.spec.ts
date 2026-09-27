import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { SessionPage } from './session-page';
import { SessionStore } from './session.store';
import { RUNTIME_CONFIG } from '../../shared/api/runtime-config';
import { authInterceptor } from '../../shared/auth/auth.interceptor';

describe('SessionPage', () => {
  beforeEach(() => {
    sessionStorage.clear();
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          { path: 'session', component: SessionPage },
          { path: '', component: SessionPage },
          { path: 'customers/:id', component: SessionPage },
        ]),
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

  async function page(): Promise<HTMLElement> {
    const fixture = TestBed.createComponent(SessionPage);
    fixture.detectChanges();
    await fixture.whenStable();
    return fixture.nativeElement as HTMLElement;
  }

  it('does not offer a password field that issues tokens', async () => {
    const element = await page();
    expect(element.querySelector('input[type="password"]')).toBeNull();
    expect(element.querySelector('#access-token')).toBeTruthy();
    expect(element.querySelector('#refresh-token')).toBeTruthy();
  });

  it('stores the user and firm name after a successful probe', async () => {
    const fixture = TestBed.createComponent(SessionPage);
    const element = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);

    setField(element, '#access-token', 'access-1');
    setField(element, '#refresh-token', 'refresh-1');
    element.querySelector('form')!.dispatchEvent(new Event('submit'));
    fixture.detectChanges();

    const http = TestBed.inject(HttpTestingController);
    const me = http.expectOne('http://api.test/users/me');
    expect(me.request.headers.get('Authorization')).toBe('Bearer access-1');
    me.flush(tenantAdmin);
    const tenant = http.expectOne('http://api.test/tenants/by-code/firm');
    tenant.flush({ id: 'tenant-1', name: 'Northwind', code: 'firm' });
    await fixture.whenStable();

    const session = TestBed.inject(SessionStore);
    expect(session.currentUser()?.name).toBe('Ada');
    expect(session.tenantName()).toBe('Northwind');
    expect(navigate).toHaveBeenCalledWith('/');
  });

  it('returns to the saved in-app path after a successful probe', async () => {
    TestBed.inject(SessionStore).setReturnTo('/customers/cust-1');
    const fixture = TestBed.createComponent(SessionPage);
    const element = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);

    setField(element, '#access-token', 'access-1');
    element.querySelector('form')!.dispatchEvent(new Event('submit'));

    const http = TestBed.inject(HttpTestingController);
    http.expectOne('http://api.test/users/me').flush({ ...tenantAdmin, tenantCode: null });
    await fixture.whenStable();

    expect(navigate).toHaveBeenCalledWith('/customers/cust-1');
    expect(TestBed.inject(SessionStore).returnTo()).toBeNull();
  });

  it('shows an error and keeps no session when the probe is rejected', async () => {
    const fixture = TestBed.createComponent(SessionPage);
    const element = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();

    setField(element, '#access-token', 'bad-token');
    element.querySelector('form')!.dispatchEvent(new Event('submit'));
    TestBed.inject(HttpTestingController)
      .expectOne('http://api.test/users/me')
      .flush({ title: 'Forbidden' }, { status: 403, statusText: 'Forbidden' });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(element.textContent).toContain('That token was not accepted.');
    expect(TestBed.inject(SessionStore).accessToken()).toBeNull();
    expect(TestBed.inject(SessionStore).currentUser()).toBeNull();
  });
});

const tenantAdmin = {
  id: 'user-1',
  name: 'Ada',
  email: 'ada@firm',
  role: 'tenantAdmin',
  status: 'active',
  tenantId: 'tenant-1',
  tenantCode: 'firm',
  adviserId: null,
  rowVersion: 'rv',
};

function setField(element: HTMLElement, selector: string, value: string): void {
  const field = element.querySelector(selector) as HTMLTextAreaElement;
  field.value = value;
  field.dispatchEvent(new Event('input'));
}
