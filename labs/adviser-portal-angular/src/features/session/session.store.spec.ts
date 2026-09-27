import { TestBed } from '@angular/core/testing';
import { SessionStore } from './session.store';
import {
  ACCESS_TOKEN_KEY,
  REFRESH_TOKEN_KEY,
  RETURN_TO_KEY,
} from './session.storage';

describe('SessionStore', () => {
  beforeEach(() => {
    sessionStorage.clear();
    TestBed.resetTestingModule();
  });

  function store(): SessionStore {
    TestBed.configureTestingModule({});
    return TestBed.inject(SessionStore);
  }

  it('writes tokens to sessionStorage', () => {
    const session = store();
    session.setTokens('access-1', 'refresh-1');

    expect(session.accessToken()).toBe('access-1');
    expect(session.refreshToken()).toBe('refresh-1');
    expect(sessionStorage.getItem(ACCESS_TOKEN_KEY)).toBe('access-1');
    expect(sessionStorage.getItem(REFRESH_TOKEN_KEY)).toBe('refresh-1');
  });

  it('removes the refresh token when one is not supplied', () => {
    sessionStorage.setItem(REFRESH_TOKEN_KEY, 'old');
    const session = store();
    session.setTokens('access-1', null);

    expect(session.refreshToken()).toBeNull();
    expect(sessionStorage.getItem(REFRESH_TOKEN_KEY)).toBeNull();
  });

  it('hydrates tokens from sessionStorage', () => {
    sessionStorage.setItem(ACCESS_TOKEN_KEY, 'stored-access');
    sessionStorage.setItem(REFRESH_TOKEN_KEY, 'stored-refresh');
    const session = store();
    session.hydrate();

    expect(session.accessToken()).toBe('stored-access');
    expect(session.refreshToken()).toBe('stored-refresh');
  });

  it('clear empties the store and sessionStorage', () => {
    const session = store();
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
    session.setTenantName('Northwind');
    session.setReturnTo('/customers/user-1');

    session.clear();

    expect(session.accessToken()).toBeNull();
    expect(session.refreshToken()).toBeNull();
    expect(session.currentUser()).toBeNull();
    expect(session.tenantName()).toBeNull();
    expect(session.returnTo()).toBeNull();
    expect(sessionStorage.length).toBe(0);
  });
});
