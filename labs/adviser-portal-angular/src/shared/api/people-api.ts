import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { CurrentUser, TenantSummary } from '../../features/session/session.models';

@Injectable({ providedIn: 'root' })
export class PeopleApi {
  private readonly http = inject(HttpClient);

  me(): Observable<CurrentUser> {
    return this.http.get<CurrentUser>('/users/me');
  }

  tenantByCode(code: string): Observable<TenantSummary> {
    return this.http.get<TenantSummary>(`/tenants/by-code/${encodeURIComponent(code)}`);
  }

  updateMe(body: { name: string; rowVersion: string }): Observable<void> {
    return this.http.put<void>('/users/me', body);
  }

  updatePassword(body: { currentPassword: string; newPassword: string }): Observable<void> {
    return this.http.put<void>('/users/me/password', body);
  }
}
