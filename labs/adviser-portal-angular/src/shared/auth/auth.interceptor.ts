import { HttpContextToken, HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, from, switchMap, throwError } from 'rxjs';
import { SessionStore } from '../../features/session/session.store';
import { RUNTIME_CONFIG } from '../api/runtime-config';

const RETRIED = new HttpContextToken<boolean>(() => false);
const PRODUCT_CLIENT_ID = 'adviser-portal';

let refreshInFlight: Promise<boolean> | null = null;

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const config = inject(RUNTIME_CONFIG);
  const session = inject(SessionStore);
  const router = inject(Router);
  const url = /^https?:\/\//.test(req.url) ? req.url : `${config.webApiBaseUrl}${req.url}`;
  const accessToken = session.accessToken();
  const authed = req.clone({
    url,
    setHeaders: accessToken ? { Authorization: `Bearer ${accessToken}` } : {},
  });

  return next(authed).pipe(
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse) || error.status !== 401) {
        return throwError(() => error);
      }

      const refreshToken = session.refreshToken();
      if (!refreshToken || req.context.get(RETRIED)) {
        endSession(session, router);
        return throwError(() => error);
      }

      return from(refreshOnce(config.identityAuthority, refreshToken, session)).pipe(
        switchMap((refreshed) => {
          if (!refreshed) {
            endSession(session, router);
            return throwError(() => error);
          }

          const retry = authed.clone({
            setHeaders: { Authorization: `Bearer ${session.accessToken()}` },
            context: authed.context.set(RETRIED, true),
          });
          return next(retry).pipe(
            catchError((retryError: unknown) => {
              if (retryError instanceof HttpErrorResponse && retryError.status === 401) {
                endSession(session, router);
              }
              return throwError(() => retryError);
            }),
          );
        }),
      );
    }),
  );
};

function endSession(session: InstanceType<typeof SessionStore>, router: Router): void {
  session.clear();
  if (!router.url.startsWith('/session')) {
    void router.navigate(['/session']);
  }
}

function refreshOnce(
  authority: string,
  refreshToken: string,
  session: InstanceType<typeof SessionStore>,
): Promise<boolean> {
  refreshInFlight ??= refreshTokens(authority, refreshToken, session).finally(() => {
    refreshInFlight = null;
  });
  return refreshInFlight;
}

async function refreshTokens(
  authority: string,
  refreshToken: string,
  session: InstanceType<typeof SessionStore>,
): Promise<boolean> {
  try {
    const response = await fetch(`${authority.replace(/\/$/, '')}/connect/token`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
      body: new URLSearchParams({
        grant_type: 'refresh_token',
        refresh_token: refreshToken,
        client_id: PRODUCT_CLIENT_ID,
      }),
    });
    if (!response.ok) {
      return false;
    }

    const body = (await response.json()) as { access_token?: string; refresh_token?: string };
    if (!body.access_token) {
      return false;
    }

    session.setTokens(body.access_token, body.refresh_token ?? refreshToken);
    return true;
  } catch {
    return false;
  }
}
