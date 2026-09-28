import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { routes } from '../../app/app.routes';
import { CurrentUser } from '../session/session.models';
import { SessionStore } from '../session/session.store';
import { RUNTIME_CONFIG } from '../../shared/api/runtime-config';
import { authInterceptor } from '../../shared/auth/auth.interceptor';

describe('customers', () => {
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
    vi.spyOn(window, 'confirm').mockReturnValue(true);
  });

  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
    vi.restoreAllMocks();
  });

  it('links tenant admins and advisers to a new customer', async () => {
    signIn(person('tenantAdmin'));
    const harness = await RouterTestingHarness.create('/customers');
    const http = TestBed.inject(HttpTestingController);
    flushCustomerList(http, true);
    harness.detectChanges();
    expect(anchor(harness.fixture.nativeElement as HTMLElement, 'New customer')?.getAttribute('href')).toBe(
      '/customers/new',
    );

    signIn(person('adviser', { id: 'adv-1', name: 'Cara', adviserId: 'adv-1' }));
    await harness.navigateByUrl('/customers');
    flushCustomerList(http, false);
    harness.detectChanges();
    expect(anchor(harness.fixture.nativeElement as HTMLElement, 'New customer')?.getAttribute('href')).toBe(
      '/customers/new',
    );
  });

  it('joins adviser names for a tenant admin and does not ask for them as an adviser', async () => {
    signIn(person('tenantAdmin'));
    const harness = await RouterTestingHarness.create('/customers');
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('http://api.test/users/customers?page=1&pageSize=20').flush({
      items: [customer],
      page: 1,
      pageSize: 20,
      totalCount: 1,
    });
    http.expectOne('http://api.test/users/advisers?page=1&pageSize=100').flush({
      items: [{ id: 'adv-1', name: 'Cara', email: 'cara@firm', status: 'active', rowVersion: 'rv', tenantId: 'tenant-1', created: '2026-01-01' }],
      page: 1,
      pageSize: 100,
      totalCount: 1,
    });
    harness.detectChanges();
    expect(harness.fixture.nativeElement.textContent).toContain('Cara');
  });

  it('shows the caller name for an adviser without requesting advisers', async () => {
    signIn(person('adviser', { id: 'adv-1', name: 'Cara', adviserId: 'adv-1' }));
    const harness = await RouterTestingHarness.create('/customers');
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('http://api.test/users/customers?page=1&pageSize=20').flush({
      items: [customer],
      page: 1,
      pageSize: 20,
      totalCount: 1,
    });
    http.expectNone('http://api.test/users/advisers?page=1&pageSize=100');
    harness.detectChanges();
    expect(harness.fixture.nativeElement.textContent).toContain('Cara');
  });

  it('sends adviserId when a tenant admin creates a customer and omits it for an adviser', async () => {
    signIn(person('tenantAdmin'));
    const harness = await RouterTestingHarness.create('/customers/new');
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('http://api.test/users/advisers?page=1&pageSize=100&enabledOnly=true').flush({
      items: [{ id: 'adv-1', name: 'Cara', email: 'cara@firm', status: 'active', rowVersion: 'rv', tenantId: 'tenant-1', created: '2026-01-01' }],
      page: 1,
      pageSize: 100,
      totalCount: 1,
    });
    harness.detectChanges();
    const element = harness.fixture.nativeElement as HTMLElement;
    setValue(element, '#customer-name', 'Jordan');
    setValue(element, '#customer-email', 'jordan@firm');
    setValue(element, '#customer-password', 'Secret-pass-1');
    setValue(element, '#customer-confirm', 'Secret-pass-1');
    setValue(element, '#customer-adviser', 'adv-1');
    element.querySelector('form')!.dispatchEvent(new Event('submit'));

    const created = http.expectOne('http://api.test/users/customers');
    expect(created.request.method).toBe('POST');
    expect(created.request.body).toEqual({
      name: 'Jordan',
      email: 'jordan@firm',
      password: 'Secret-pass-1',
      adviserId: 'adv-1',
    });
    created.flush({ id: 'cust-9' });
    await harness.fixture.whenStable();
    expect(TestBed.inject(Router).url).toBe('/customers/cust-9');
    http.expectOne('http://api.test/users/customers/cust-9').flush(customer);
    harness.detectChanges();
    expect(harness.fixture.nativeElement.textContent).not.toContain('Secret-pass-1');

    signIn(person('adviser', { id: 'adv-1', adviserId: 'adv-1' }));
    await harness.navigateByUrl('/customers/new');
    http.expectNone('http://api.test/users/advisers?page=1&pageSize=100&enabledOnly=true');
    const adviserElement = harness.fixture.nativeElement as HTMLElement;
    expect(adviserElement.querySelector('#customer-adviser')).toBeNull();
    setValue(adviserElement, '#customer-name', 'Pat');
    setValue(adviserElement, '#customer-email', 'pat@firm');
    setValue(adviserElement, '#customer-password', 'Secret-pass-1');
    setValue(adviserElement, '#customer-confirm', 'Secret-pass-1');
    adviserElement.querySelector('form')!.dispatchEvent(new Event('submit'));
    const adviserCreated = http.expectOne('http://api.test/users/customers');
    expect(adviserCreated.request.body).toEqual({
      name: 'Pat',
      email: 'pat@firm',
      password: 'Secret-pass-1',
    });
    expect(adviserCreated.request.body).not.toHaveProperty('adviserId');
    adviserCreated.flush({ id: 'cust-8' });
  });

  it('keeps detail read-only and lets a tenant admin change the adviser while editing', async () => {
    signIn(person('tenantAdmin'));
    const harness = await RouterTestingHarness.create('/customers/cust-1');
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('http://api.test/users/customers/cust-1').flush(customer);
    harness.detectChanges();
    const detail = harness.fixture.nativeElement as HTMLElement;
    expect(detail.querySelector('#edit-customer-name')).toBeNull();
    expect(detail.querySelector('#edit-customer-adviser')).toBeNull();
    expect(detail.textContent).toContain('Disable');

    await harness.navigateByUrl('/customers/cust-1/edit');
    http.expectOne('http://api.test/users/customers/cust-1').flush(customer);
    http.expectOne('http://api.test/users/advisers?page=1&pageSize=100&enabledOnly=true').flush({
      items: [
        { id: 'adv-1', name: 'Cara', email: 'cara@firm', status: 'active', rowVersion: 'rv', tenantId: 'tenant-1', created: '2026-01-01' },
        { id: 'adv-2', name: 'Ned', email: 'ned@firm', status: 'active', rowVersion: 'rv', tenantId: 'tenant-1', created: '2026-01-01' },
      ],
      page: 1,
      pageSize: 100,
      totalCount: 2,
    });
    harness.detectChanges();
    const edit = harness.fixture.nativeElement as HTMLElement;
    expect(edit.querySelector('#edit-customer-adviser')).toBeTruthy();
    setValue(edit, '#edit-customer-name', 'Jordan Lee');
    setValue(edit, '#edit-customer-adviser', 'adv-2');
    edit.querySelector('form')!.dispatchEvent(new Event('submit'));
    const update = http.expectOne('http://api.test/users/customers/cust-1');
    expect(update.request.method).toBe('PUT');
    expect(update.request.body).toEqual({ name: 'Jordan Lee', adviserId: 'adv-2', rowVersion: 'rv-1' });
    update.flush(null);
  });

  it('hides the adviser control when an adviser edits a customer', async () => {
    signIn(person('adviser', { id: 'adv-1', adviserId: 'adv-1' }));
    const harness = await RouterTestingHarness.create('/customers/cust-1/edit');
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('http://api.test/users/customers/cust-1').flush(customer);
    harness.detectChanges();
    expect(harness.fixture.nativeElement.querySelector('#edit-customer-adviser')).toBeNull();
  });

  it('sends the current row version and tells the user to reload when it is stale', async () => {
    signIn(person('tenantAdmin'));
    const harness = await RouterTestingHarness.create('/customers/cust-1');
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('http://api.test/users/customers/cust-1').flush(customer);
    harness.detectChanges();
    clickButton(harness.fixture.nativeElement as HTMLElement, 'Disable');
    const disable = http.expectOne('http://api.test/users/customers/cust-1/disable');
    expect(disable.request.body).toEqual({ rowVersion: 'rv-1' });
    disable.flush({ title: 'Conflict' }, { status: 409, statusText: 'Conflict' });
    harness.detectChanges();
    expect(harness.fixture.nativeElement.textContent).toContain('This row changed. Reload and try again.');
    http.expectNone('http://api.test/users/customers/cust-1/disable');
  });

  it('shows not found on the customer url', async () => {
    signIn(person('tenantAdmin'));
    const harness = await RouterTestingHarness.create('/customers/missing');
    TestBed.inject(HttpTestingController)
      .expectOne('http://api.test/users/customers/missing')
      .flush({ title: 'Not found' }, { status: 404, statusText: 'Not Found' });
    harness.detectChanges();
    expect(TestBed.inject(Router).url).toBe('/customers/missing');
    expect(harness.fixture.nativeElement.textContent).toContain('Not found');
  });
});

const customer = {
  id: 'cust-1',
  tenantId: 'tenant-1',
  adviserId: 'adv-1',
  name: 'Jordan',
  email: 'jordan@firm',
  status: 'active',
  rowVersion: 'rv-1',
  created: '2026-01-02T00:00:00Z',
};

function signIn(user: CurrentUser): void {
  const session = TestBed.inject(SessionStore);
  session.setTokens('access-1', null);
  session.setCurrentUser(user);
  session.setTenantName('Northwind');
}

function person(role: string, extra: Partial<CurrentUser> = {}): CurrentUser {
  return {
    id: 'user-1',
    name: 'Ada',
    email: 'ada@firm',
    role,
    status: 'active',
    tenantId: 'tenant-1',
    tenantCode: 'firm',
    adviserId: null,
    rowVersion: 'rv',
    ...extra,
  };
}

function flushCustomerList(http: HttpTestingController, tenantAdmin: boolean): void {
  http.expectOne('http://api.test/users/customers?page=1&pageSize=20').flush({
    items: [],
    page: 1,
    pageSize: 20,
    totalCount: 0,
  });
  if (tenantAdmin) {
    http.expectOne('http://api.test/users/advisers?page=1&pageSize=100').flush({
      items: [],
      page: 1,
      pageSize: 100,
      totalCount: 0,
    });
  } else {
    http.expectNone('http://api.test/users/advisers?page=1&pageSize=100');
  }
}

function anchor(element: HTMLElement, label: string): HTMLAnchorElement | undefined {
  return [...element.querySelectorAll('a')].find((item) => item.textContent?.trim() === label);
}

function setValue(element: HTMLElement, selector: string, value: string): void {
  const field = element.querySelector(selector) as HTMLInputElement | HTMLSelectElement;
  field.value = value;
  field.dispatchEvent(new Event('input'));
  field.dispatchEvent(new Event('change'));
}

function clickButton(element: HTMLElement, label: string): void {
  const button = [...element.querySelectorAll('button')].find((item) => item.textContent?.includes(label));
  button?.dispatchEvent(new Event('click'));
}
