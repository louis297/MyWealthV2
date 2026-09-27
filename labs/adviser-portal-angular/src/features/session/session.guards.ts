import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { isInAppPath } from './session.paths';
import { SessionStore } from './session.store';
import { roles } from './roles';

export const requireSession: CanActivateFn = (_route, state) => {
  const session = inject(SessionStore);
  const router = inject(Router);
  if (!session.accessToken()) {
    if (isInAppPath(state.url) && !state.url.startsWith('/session')) {
      session.setReturnTo(state.url);
    }
    return router.createUrlTree(['/session']);
  }

  if (session.currentUser()?.role === roles.customer && !state.url.startsWith('/forbidden')) {
    return router.createUrlTree(['/forbidden']);
  }

  return true;
};

export const homeRedirect: CanActivateFn = () => {
  const role = inject(SessionStore).currentUser()?.role;
  const router = inject(Router);
  if (role === roles.tenantAdmin || role === roles.adviser) {
    return router.createUrlTree(['/customers']);
  }
  if (role === roles.customer) {
    return router.createUrlTree(['/forbidden']);
  }
  return router.createUrlTree(['/profile']);
};

export function allowRoles(allowed: readonly string[]): CanActivateFn {
  return () => {
    const role = inject(SessionStore).currentUser()?.role;
    if (role && allowed.includes(role)) {
      return true;
    }
    return inject(Router).createUrlTree(['/forbidden']);
  };
}
