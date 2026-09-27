import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { AdviserList, Customer, CustomerList } from './customer.models';

@Injectable({ providedIn: 'root' })
export class CustomersApi {
  private readonly http = inject(HttpClient);

  list(args: { page: number; search?: string; enabledOnly?: boolean; adviserId?: string }): Observable<CustomerList> {
    const params = new URLSearchParams();
    params.set('page', String(args.page));
    params.set('pageSize', '20');
    if (args.search) {
      params.set('search', args.search);
    }
    if (args.enabledOnly) {
      params.set('enabledOnly', 'true');
    }
    if (args.adviserId) {
      params.set('adviserId', args.adviserId);
    }
    return this.http.get<CustomerList>(`/users/customers?${params.toString()}`);
  }

  get(id: string): Observable<Customer> {
    return this.http.get<Customer>(`/users/customers/${id}`);
  }

  advisers(enabledOnly = false): Observable<AdviserList> {
    const params = new URLSearchParams();
    params.set('page', '1');
    params.set('pageSize', '100');
    if (enabledOnly) {
      params.set('enabledOnly', 'true');
    }
    return this.http.get<AdviserList>(`/users/advisers?${params.toString()}`);
  }

  create(body: { name: string; email: string; password: string; adviserId?: string }): Observable<{ id: string }> {
    return this.http.post<{ id: string }>('/users/customers', body);
  }

  update(id: string, body: { name: string; rowVersion: string; adviserId?: string }): Observable<void> {
    return this.http.put<void>(`/users/customers/${id}`, body);
  }

  changeStatus(id: string, action: 'disable' | 'enable', rowVersion: string): Observable<void> {
    return this.http.post<void>(`/users/customers/${id}/${action}`, { rowVersion });
  }
}
