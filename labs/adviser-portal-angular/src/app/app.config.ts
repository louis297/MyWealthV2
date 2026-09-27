import { provideHttpClient, withInterceptors } from '@angular/common/http';
import {
  ApplicationConfig,
  inject,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
} from '@angular/core';
import { provideRouter } from '@angular/router';
import { SessionStore } from '../features/session/session.store';
import { RUNTIME_CONFIG } from '../shared/api/runtime-config';
import { authInterceptor } from '../shared/auth/auth.interceptor';
import { routes } from './app.routes';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes),
    provideHttpClient(withInterceptors([authInterceptor])),
    provideAppInitializer(() => {
      inject(SessionStore).hydrate();
    }),
    provideAppInitializer(async () => {
      const config = inject(RUNTIME_CONFIG);
      try {
        const response = await fetch('/assets/runtime-config.json');
        if (!response.ok) {
          return;
        }
        const body = (await response.json()) as {
          webApiBaseUrl?: string;
          identityAuthority?: string;
        };
        config.webApiBaseUrl = (body.webApiBaseUrl ?? '').replace(/\/$/, '');
        config.identityAuthority = (body.identityAuthority ?? '').replace(/\/$/, '');
      } catch {
        // A missing dev config leaves the base URLs empty until `npm start` writes the file.
      }
    }),
  ],
};
