import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
  type TestRequest,
} from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
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

  it('clears the session and opens /session on 401 when no refresh token is stored', async () => {
    const session = TestBed.inject(SessionStore);
    session.setTokens('access-1', null);
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    const pending = firstValueFrom(TestBed.inject(HttpClient).get('/users/me'));
    TestBed.inject(HttpTestingController)
      .expectOne('http://api.test/users/me')
      .flush(null, { status: 401, statusText: 'Unauthorized' });

    await expect(pending).rejects.toBeTruthy();
    expect(session.accessToken()).toBeNull();
    expect(sessionStorage.length).toBe(0);
    expect(navigate).toHaveBeenCalledWith(['/session']);
  });

  it('refreshes once and retries with the new access token', async () => {
    const session = TestBed.inject(SessionStore);
    session.setTokens('old-access', 'refresh-1');
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      json: async () => ({ access_token: 'new-access', refresh_token: 'refresh-2' }),
    });
    vi.stubGlobal('fetch', fetchMock);

    const pending = firstValueFrom(TestBed.inject(HttpClient).get('/users/me'));
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('http://api.test/users/me').flush(null, { status: 401, statusText: 'Unauthorized' });

    await vi.waitUntil(() => fetchMock.mock.calls.length === 1);
    const [, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(String(init.body)).toContain('grant_type=refresh_token');
    expect(String(init.body)).toContain('client_id=adviser-portal');
    expect(String(init.body)).not.toContain('adviser-portal-angular');

    const retry = await pendingRequest(http);
    expect(retry.request.headers.get('Authorization')).toBe('Bearer new-access');
    retry.flush({ id: 'user-1' });
    await pending;

    expect(session.accessToken()).toBe('new-access');
    expect(session.refreshToken()).toBe('refresh-2');
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it('does not refresh a second time when the retry is also 401', async () => {
    const session = TestBed.inject(SessionStore);
    session.setTokens('old-access', 'refresh-1');
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      json: async () => ({ access_token: 'new-access', refresh_token: 'refresh-2' }),
    });
    vi.stubGlobal('fetch', fetchMock);
    vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

    const pending = firstValueFrom(TestBed.inject(HttpClient).get('/users/me'));
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('http://api.test/users/me').flush(null, { status: 401, statusText: 'Unauthorized' });
    await vi.waitUntil(() => fetchMock.mock.calls.length === 1);
    (await pendingRequest(http)).flush(null, { status: 401, statusText: 'Unauthorized' });

    await expect(pending).rejects.toBeTruthy();
    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(session.accessToken()).toBeNull();
    expect(sessionStorage.length).toBe(0);
  });
});

async function pendingRequest(http: HttpTestingController): Promise<TestRequest> {
  let request: TestRequest | undefined;
  await vi.waitFor(() => {
    const matches = http.match('http://api.test/users/me');
    expect(matches.length).toBe(1);
    request = matches[0];
  });
  return request!;
}
