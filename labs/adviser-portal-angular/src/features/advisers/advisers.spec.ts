import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { routes } from '../../app/app.routes';
import { RUNTIME_CONFIG } from '../../shared/api/runtime-config';
import { authInterceptor } from '../../shared/auth/auth.interceptor';
import { SessionStore } from '../session/session.store';

const assignedCustomersMessage =
  'Reassign or disable assigned customers before disabling this adviser.';

describe('advisers', () => {
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

  it('creates and renames an adviser', async () => {
    signIn();
    const harness = await RouterTestingHarness.create('/advisers/new');
    const http = TestBed.inject(HttpTestingController);
    const element = harness.fixture.nativeElement as HTMLElement;
    setValue(element, '#adviser-name', 'Cara');
    setValue(element, '#adviser-email', 'cara@firm');
    setValue(element, '#adviser-password', 'Secret-pass-1');
    setValue(element, '#adviser-confirm', 'Secret-pass-1');
    element.querySelector('form')!.dispatchEvent(new Event('submit'));

    const created = http.expectOne('http://api.test/users/advisers');
    expect(created.request.body).toEqual({
      name: 'Cara',
      email: 'cara@firm',
      password: 'Secret-pass-1',
    });
    created.flush({ id: 'adv-9' });
    await harness.fixture.whenStable();
    expect(TestBed.inject(Router).url).toBe('/advisers/adv-9');
    http.expectOne('http://api.test/users/advisers/adv-9').flush(adviser);

    await harness.navigateByUrl('/advisers/adv-9/edit');
    http.expectOne('http://api.test/users/advisers/adv-9').flush({ ...adviser, id: 'adv-9' });
    harness.detectChanges();
    const edit = harness.fixture.nativeElement as HTMLElement;
    setValue(edit, '#edit-adviser-name', 'Cara Lee');
    edit.querySelector('form')!.dispatchEvent(new Event('submit'));
    const update = http.expectOne('http://api.test/users/advisers/adv-9');
    expect(update.request.method).toBe('PUT');
    expect(update.request.body).toEqual({ name: 'Cara Lee', rowVersion: 'rv-1' });
    update.flush(null);
  });

  it('links to assigned customers when disable is rejected', async () => {
    signIn();
    const harness = await RouterTestingHarness.create('/advisers/adv-1');
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('http://api.test/users/advisers/adv-1').flush(adviser);
    harness.detectChanges();
    const button = [...(harness.fixture.nativeElement as HTMLElement).querySelectorAll('button')].find((item) =>
      item.textContent?.includes('Disable'),
    ) as HTMLButtonElement;
    button.click();

    const disable = http.expectOne('http://api.test/users/advisers/adv-1/disable');
    expect(disable.request.body).toEqual({ rowVersion: 'rv-1' });
    disable.flush(
      { errors: { '': [assignedCustomersMessage] } },
      { status: 400, statusText: 'Bad Request' },
    );
    harness.detectChanges();

    const root = harness.fixture.nativeElement as HTMLElement;
    expect(root.textContent).toContain(assignedCustomersMessage);
    expect(root.querySelector('a[href="/customers?adviserId=adv-1"]')).toBeTruthy();
  });
});

const adviser = {
  id: 'adv-1',
  tenantId: 'tenant-1',
  name: 'Cara',
  email: 'cara@firm',
  status: 'active',
  rowVersion: 'rv-1',
  created: '2026-01-02T00:00:00Z',
};

function signIn(): void {
  TestBed.inject(SessionStore).setTokens('access-1', null);
  TestBed.inject(SessionStore).setCurrentUser({
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
}

function setValue(element: HTMLElement, selector: string, value: string): void {
  const field = element.querySelector(selector) as HTMLInputElement;
  field.value = value;
  field.dispatchEvent(new Event('input'));
}
