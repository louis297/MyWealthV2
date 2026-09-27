import { Component, inject, OnInit, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { SessionStore } from '../session/session.store';
import { roles } from '../session/roles';
import { Customer } from './customer.models';
import { CustomersApi } from './customers-api';

@Component({
  selector: 'app-customers-list-page',
  imports: [RouterLink],
  templateUrl: './customers-list-page.html',
})
export class CustomersListPage implements OnInit {
  private readonly api = inject(CustomersApi);
  private readonly route = inject(ActivatedRoute);
  private readonly session = inject(SessionStore);

  protected readonly customers = signal<Customer[]>([]);
  protected readonly adviserNames = signal<Map<string, string>>(new Map());

  ngOnInit(): void {
    const params = this.route.snapshot.queryParamMap;
    const adviserId = params.get('adviserId') ?? '';
    this.api
      .list({
        page: Number(params.get('page') || '1') || 1,
        search: params.get('search') || undefined,
        enabledOnly: params.get('enabledOnly') === 'true',
        adviserId: this.isTenantAdmin() && adviserId ? adviserId : undefined,
      })
      .subscribe({
        next: (page) => this.customers.set(page.items),
        error: () => this.customers.set([]),
      });

    if (this.isTenantAdmin()) {
      this.api.advisers().subscribe({
        next: (page) => this.adviserNames.set(new Map(page.items.map((item) => [item.id, item.name]))),
        error: () => this.adviserNames.set(new Map()),
      });
    }
  }

  protected adviserName(adviserId: string): string {
    if (!this.isTenantAdmin()) {
      return this.session.currentUser()?.name ?? '';
    }
    return this.adviserNames().get(adviserId) ?? '';
  }

  private isTenantAdmin(): boolean {
    return this.session.currentUser()?.role === roles.tenantAdmin;
  }
}
