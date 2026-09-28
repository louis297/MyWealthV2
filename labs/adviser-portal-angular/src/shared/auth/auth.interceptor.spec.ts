import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
  type TestRequest,
} from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { SessionStore } from '../../features/session/session.store';
import { RUNTIME_CONFIG } from '../api/runtime-config';
import { authInterceptor } from './auth.interceptor';

describe('authInterceptor', () => {
  beforeEach(() => {
    sessionStorage.clear();
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([{ path: 'session', children: [] }]),
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        {
          provide: RUNTIME_CONFIG,
          useValue: {
            webApiBaseUrl: 'http://api.test',
            identityAuthority: 'http://identity.test',
          },
        },
      ],
    });
  });

  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
    vi.unstubAllGlobals();
  });

  it('sends the bearer token to the webapi origin', async () => {
    TestBed.inject(SessionStore).setTokens('access-1', null);
    const pending = firstValueFrom(TestBed.inject(HttpClient).get('/users/me'));
    const request = TestBed.inject(HttpTestingController).expectOne('http://api.test/users/me');

    expect(request.request.headers.get('Authorization')).toBe('Bearer access-1');
    request.flush({ id: 'user-1' });
    await pending;
  });

  it('starts authorize on 401 when no refresh token is stored', async () => {
    const session = TestBed.inject(SessionStore);
    session.setTokens('access-1', null);
    const assign = stubIdentity();
    const pending = firstValueFrom(TestBed.inject(HttpClient).get('/users/me'));
    TestBed.inject(HttpTestingController)
      .expectOne('http://api.test/users/me')
      .flush(null, { status: 401, statusText: 'Unauthorized' });

    await expect(pending).rejects.toBeTruthy();
    await vi.waitUntil(() => assign.mock.calls.length === 1);
    expect(session.accessToken()).toBeNull();
    expect(new URL(assign.mock.calls[0][0] as string).searchParams.get('client_id')).toBe(
      'adviser-portal-angular',
    );
  });

  it('refreshes once at the discovered token endpoint for this client', async () => {
    const session = TestBed.inject(SessionStore);
    session.setTokens('old-access', 'refresh-1');
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce({ ok: true, json: async () => discovered })
      .mockResolvedValueOnce({
        ok: true,
        json: async () => ({ access_token: 'new-access', refresh_token: 'refresh-2' }),
      });
    vi.stubGlobal('fetch', fetchMock);

    const pending = firstValueFrom(TestBed.inject(HttpClient).get('/users/me'));
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('http://api.test/users/me').flush(null, { status: 401, statusText: 'Unauthorized' });

    await vi.waitUntil(() => fetchMock.mock.calls.length === 2);
    expect(fetchMock.mock.calls[0][0]).toBe('http://identity.test/.well-known/openid-configuration');
    expect(fetchMock.mock.calls[1][0]).toBe('https://identity.test/connect/token');
    const body = String((fetchMock.mock.calls[1][1] as RequestInit).body);
    expect(body).toContain('grant_type=refresh_token');
    expect(body).toContain('client_id=adviser-portal-angular');
    expect(body).not.toContain('client_id=adviser-portal&');

    const retry = await pendingRequest(http);
    expect(retry.request.headers.get('Authorization')).toBe('Bearer new-access');
    retry.flush({ id: 'user-1' });
    await pending;

    expect(session.accessToken()).toBe('new-access');
    expect(session.refreshToken()).toBe('refresh-2');
    expect(fetchMock.mock.calls.filter(([url]) => url === 'https://identity.test/connect/token')).toHaveLength(1);
  });

  it('starts authorize when refresh fails and does not authorize during sign-out', async () => {
    const session = TestBed.inject(SessionStore);
    session.setTokens('old-access', 'refresh-1');
    const assign = stubIdentity();
    vi.stubGlobal(
      'fetch',
      vi.fn().mockImplementation(async (input: string) => {
        if (String(input).includes('openid-configuration')) {
          return { ok: true, json: async () => discovered };
        }
        return { ok: false, json: async () => ({}) };
      }),
    );

    const pending = firstValueFrom(TestBed.inject(HttpClient).get('/users/me'));
    TestBed.inject(HttpTestingController)
      .expectOne('http://api.test/users/me')
      .flush(null, { status: 401, statusText: 'Unauthorized' });

    await expect(pending).rejects.toBeTruthy();
    await vi.waitUntil(() => assign.mock.calls.length === 1);
    expect(session.accessToken()).toBeNull();

    sessionStorage.clear();
    sessionStorage.setItem('adviser-portal-angular.signOutInProgress', '1');
    session.setTokens('old-access', 'refresh-1');
    assign.mockClear();
    const duringSignOut = firstValueFrom(TestBed.inject(HttpClient).get('/users/me'));
    TestBed.inject(HttpTestingController)
      .expectOne('http://api.test/users/me')
      .flush(null, { status: 401, statusText: 'Unauthorized' });
    await expect(duringSignOut).rejects.toBeTruthy();
    await Promise.resolve();
    expect(assign).not.toHaveBeenCalled();
  });

  it('does not refresh a second time when the retry is also 401', async () => {
    const session = TestBed.inject(SessionStore);
    session.setTokens('old-access', 'refresh-1');
    const assign = stubIdentity();
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce({ ok: true, json: async () => discovered })
      .mockResolvedValueOnce({
        ok: true,
        json: async () => ({ access_token: 'new-access', refresh_token: 'refresh-2' }),
      })
      .mockResolvedValue({ ok: true, json: async () => discovered });
    vi.stubGlobal('fetch', fetchMock);

    const pending = firstValueFrom(TestBed.inject(HttpClient).get('/users/me'));
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('http://api.test/users/me').flush(null, { status: 401, statusText: 'Unauthorized' });
    await vi.waitUntil(() => fetchMock.mock.calls.length === 2);
    (await pendingRequest(http)).flush(null, { status: 401, statusText: 'Unauthorized' });

    await expect(pending).rejects.toBeTruthy();
    await vi.waitUntil(() => assign.mock.calls.length === 1);
    expect(fetchMock.mock.calls.filter(([url]) => url === 'https://identity.test/connect/token')).toHaveLength(1);
    expect(session.accessToken()).toBeNull();
  });
});

const discovered = {
  authorization_endpoint: 'https://identity.test/connect/authorize',
  token_endpoint: 'https://identity.test/connect/token',
};

function stubIdentity() {
  const assign = vi.fn();
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: true, json: async () => discovered }));
  vi.stubGlobal('location', {
    origin: 'http://localhost:4200',
    href: 'http://localhost:4200/customers',
    pathname: '/customers',
    search: '',
    assign,
  });
  return assign;
}

async function pendingRequest(http: HttpTestingController): Promise<TestRequest> {
  let request: TestRequest | undefined;
  await vi.waitFor(() => {
    const matches = http.match('http://api.test/users/me');
    expect(matches.length).toBe(1);
    request = matches[0];
  });
  return request!;
}
