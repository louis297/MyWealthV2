import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { SessionPage } from './session-page';
import { RUNTIME_CONFIG } from '../../shared/api/runtime-config';
import { authInterceptor } from '../../shared/auth/auth.interceptor';

describe('SessionPage', () => {
  beforeEach(() => {
    sessionStorage.clear();
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
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
    vi.unstubAllGlobals();
  });

  it('redirects to identity and does not ask for a token or password', async () => {
    const assign = vi.fn();
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({
      ok: true,
      json: async () => ({ authorization_endpoint: 'https://identity.test/connect/authorize' }),
    }));
    vi.stubGlobal('location', {
      origin: 'http://localhost:4200',
      href: 'http://localhost:4200/session',
      pathname: '/session',
      search: '',
      assign,
    });

    const fixture = TestBed.createComponent(SessionPage);
    fixture.detectChanges();
    await vi.waitUntil(() => assign.mock.calls.length === 1);
    const element = fixture.nativeElement as HTMLElement;

    expect(element.textContent).toContain('Redirecting to sign in');
    expect(element.querySelector('#access-token')).toBeNull();
    expect(element.querySelector('#refresh-token')).toBeNull();
    expect(element.querySelector('input[type="password"]')).toBeNull();
    expect(new URL(assign.mock.calls[0][0] as string).searchParams.get('client_id')).toBe(
      'adviser-portal-angular',
    );
  });
});
