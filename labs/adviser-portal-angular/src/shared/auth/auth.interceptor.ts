import { HttpContextToken, HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, from, switchMap, throwError } from 'rxjs';
import { isSignOutInProgress, refreshTokens, startAuthorize } from '../../features/session/oidc';
import { SessionStore } from '../../features/session/session.store';
import { RUNTIME_CONFIG } from '../api/runtime-config';

const RETRIED = new HttpContextToken<boolean>(() => false);

let refreshInFlight: Promise<boolean> | null = null;

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const config = inject(RUNTIME_CONFIG);
  const session = inject(SessionStore);
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
        endSession(session, config.identityAuthority);
        return throwError(() => error);
      }

      return from(refreshOnce(config.identityAuthority, refreshToken, session)).pipe(
        switchMap((refreshed) => {
          if (!refreshed) {
            endSession(session, config.identityAuthority);
            return throwError(() => error);
          }

          const retry = authed.clone({
            setHeaders: { Authorization: `Bearer ${session.accessToken()}` },
            context: authed.context.set(RETRIED, true),
          });
          return next(retry).pipe(
            catchError((retryError: unknown) => {
              if (retryError instanceof HttpErrorResponse && retryError.status === 401) {
                endSession(session, config.identityAuthority);
              }
              return throwError(() => retryError);
            }),
          );
        }),
      );
    }),
  );
};

function endSession(session: InstanceType<typeof SessionStore>, authority: string): void {
  session.clear();
  if (!isSignOutInProgress()) {
    void startAuthorize(authority);
  }
}

function refreshOnce(
  authority: string,
  refreshToken: string,
  session: InstanceType<typeof SessionStore>,
): Promise<boolean> {
  refreshInFlight ??= applyRefresh(authority, refreshToken, session).finally(() => {
    refreshInFlight = null;
  });
  return refreshInFlight;
}

async function applyRefresh(
  authority: string,
  refreshToken: string,
  session: InstanceType<typeof SessionStore>,
): Promise<boolean> {
  const tokens = await refreshTokens(authority, refreshToken);
  if (!tokens) {
    return false;
  }

  session.setTokens(tokens.accessToken, tokens.refreshToken);
  return true;
}
