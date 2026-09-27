import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { routes } from './app.routes';
import { SessionStore } from '../features/session/session.store';
import { CurrentUser } from '../features/session/session.models';

describe('role shell', () => {
  beforeEach(() => {
    sessionStorage.clear();
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [provideRouter(routes)],
    });
  });

  it('sends a signed-out visit to /customers to the session page', async () => {
    const harness = await RouterTestingHarness.create('/customers');
    expect(TestBed.inject(Router).url).toBe('/session');
    expect(harness.fixture.nativeElement.querySelector('#access-token')).toBeTruthy();
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

  it('sends a customer token to the forbidden page', async () => {
    signIn(customer());
    const harness = await RouterTestingHarness.create('/customers');
    const text = forbiddenText(harness);
    expect(text).toContain('You do not have access to this page.');
    expect(text).toContain('A token is present, but this page is not allowed.');
  });
});

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
