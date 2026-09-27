import { patchState, signalStore, withMethods, withState } from '@ngrx/signals';
import { CurrentUser } from './session.models';
import { ACCESS_TOKEN_KEY, REFRESH_TOKEN_KEY, RETURN_TO_KEY } from './session.storage';

interface SessionState {
  accessToken: string | null;
  refreshToken: string | null;
  currentUser: CurrentUser | null;
  tenantName: string | null;
  returnTo: string | null;
}

const initialState: SessionState = {
  accessToken: null,
  refreshToken: null,
  currentUser: null,
  tenantName: null,
  returnTo: null,
};

export const SessionStore = signalStore(
  { providedIn: 'root' },
  withState(initialState),
  withMethods((store) => ({
    hydrate(): void {
      patchState(store, {
        accessToken: sessionStorage.getItem(ACCESS_TOKEN_KEY),
        refreshToken: sessionStorage.getItem(REFRESH_TOKEN_KEY),
        returnTo: sessionStorage.getItem(RETURN_TO_KEY),
      });
    },
    setTokens(accessToken: string, refreshToken: string | null): void {
      sessionStorage.setItem(ACCESS_TOKEN_KEY, accessToken);
      if (refreshToken) {
        sessionStorage.setItem(REFRESH_TOKEN_KEY, refreshToken);
      } else {
        sessionStorage.removeItem(REFRESH_TOKEN_KEY);
      }
      patchState(store, { accessToken, refreshToken });
    },
    setCurrentUser(currentUser: CurrentUser | null): void {
      patchState(store, { currentUser });
    },
    setTenantName(tenantName: string | null): void {
      patchState(store, { tenantName });
    },
    setReturnTo(returnTo: string | null): void {
      if (returnTo) {
        sessionStorage.setItem(RETURN_TO_KEY, returnTo);
      } else {
        sessionStorage.removeItem(RETURN_TO_KEY);
      }
      patchState(store, { returnTo });
    },
    clear(): void {
      sessionStorage.removeItem(ACCESS_TOKEN_KEY);
      sessionStorage.removeItem(REFRESH_TOKEN_KEY);
      sessionStorage.removeItem(RETURN_TO_KEY);
      patchState(store, initialState);
    },
  })),
);
