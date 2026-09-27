import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';

export interface Adviser {
  id: string;
  tenantId: string;
  name: string;
  email: string;
  status: string;
  rowVersion: string;
  created: string;
}

export interface AdviserPage {
  items: Adviser[];
  page: number;
  pageSize: number;
  totalCount: number;
}

@Injectable({ providedIn: 'root' })
export class AdvisersApi {
  private readonly http = inject(HttpClient);

  list(): Observable<AdviserPage> {
    return this.http.get<AdviserPage>('/users/advisers?page=1&pageSize=20');
  }

  get(id: string): Observable<Adviser> {
    return this.http.get<Adviser>(`/users/advisers/${id}`);
  }

  create(body: { name: string; email: string; password: string }): Observable<{ id: string }> {
    return this.http.post<{ id: string }>('/users/advisers', body);
  }

  update(id: string, body: { name: string; rowVersion: string }): Observable<void> {
    return this.http.put<void>(`/users/advisers/${id}`, body);
  }

  changeStatus(id: string, action: 'disable' | 'enable', rowVersion: string): Observable<void> {
    return this.http.post<void>(`/users/advisers/${id}/${action}`, { rowVersion });
  }
}
