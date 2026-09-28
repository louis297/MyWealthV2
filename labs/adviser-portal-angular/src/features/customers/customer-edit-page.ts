import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { MatButton } from '@angular/material/button';
import { MatFormField, MatLabel } from '@angular/material/form-field';
import { MatInput } from '@angular/material/input';
import { roles } from '../session/roles';
import { SessionStore } from '../session/session.store';
import { AdviserOption, Customer } from './customer.models';
import { CustomersApi } from './customers-api';

@Component({
  selector: 'app-customer-edit-page',
  imports: [FormsModule, MatFormField, MatLabel, MatInput, MatButton],
  templateUrl: './customer-edit-page.html',
})
export class CustomerEditPage implements OnInit {
  private readonly api = inject(CustomersApi);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly session = inject(SessionStore);

  protected readonly customer = signal<Customer | null>(null);
  protected readonly advisers = signal<AdviserOption[]>([]);
  protected readonly notFound = signal(false);
  protected readonly conflict = signal(false);
  protected readonly isTenantAdmin = this.session.currentUser()?.role === roles.tenantAdmin;
  protected adviserId = '';

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id') ?? '';
    this.api.get(id).subscribe({
      next: (customer) => {
        this.customer.set(customer);
        this.adviserId = customer.adviserId;
      },
      error: () => this.notFound.set(true),
    });
    if (this.isTenantAdmin) {
      this.api.advisers(true).subscribe((page) => this.advisers.set(page.items));
    }
  }

  selectValue(event: Event): string {
    return (event.target as HTMLSelectElement).value;
  }

  submit(customer: Customer, name: string): void {
    const body: { name: string; rowVersion: string; adviserId?: string } = {
      name: name.trim(),
      rowVersion: customer.rowVersion,
    };
    if (this.isTenantAdmin) {
      body.adviserId = this.adviserId;
    }
    this.api.update(customer.id, body).subscribe({
      next: () => {
        void this.router.navigate(['/customers', customer.id]);
      },
      error: (error: unknown) => {
        if (error instanceof HttpErrorResponse && error.status === 409) {
          this.conflict.set(true);
        }
      },
    });
  }
}
