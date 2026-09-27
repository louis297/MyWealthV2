import { InjectionToken } from '@angular/core';

export interface RuntimeConfig {
  webApiBaseUrl: string;
  identityAuthority: string;
}

export const RUNTIME_CONFIG = new InjectionToken<RuntimeConfig>('RUNTIME_CONFIG', {
  providedIn: 'root',
  factory: () => ({ webApiBaseUrl: '', identityAuthority: '' }),
});
