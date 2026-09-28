import { Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { MatButton } from '@angular/material/button';
import { MatFormField, MatLabel } from '@angular/material/form-field';
import { MatInput } from '@angular/material/input';
import { roles } from '../session/roles';
import { SessionStore } from '../session/session.store';
import { AdviserOption } from './customer.models';
import { CustomersApi } from './customers-api';

@Component({
  selector: 'app-customer-create-page',
  imports: [FormsModule, MatFormField, MatLabel, MatInput, MatButton],
  templateUrl: './customer-create-page.html',
})
export class CustomerCreatePage implements OnInit {
  private readonly api = inject(CustomersApi);
  private readonly session = inject(SessionStore);
  private readonly router = inject(Router);

  protected readonly advisers = signal<AdviserOption[]>([]);
  protected readonly error = signal<string | null>(null);
  protected readonly isTenantAdmin = this.session.currentUser()?.role === roles.tenantAdmin;
  protected adviserId = '';

  ngOnInit(): void {
    if (!this.isTenantAdmin) {
      return;
    }
    this.api.advisers(true).subscribe((page) => {
      this.advisers.set(page.items.filter((item) => item.status === 'active'));
    });
  }

  selectValue(event: Event): string {
    return (event.target as HTMLSelectElement).value;
  }

  submit(name: string, email: string, password: string, confirm: string, adviserId: string): void {
    if (password !== confirm) {
      this.error.set('Passwords do not match.');
      return;
    }

    if (this.isTenantAdmin && !this.adviserId) {
      this.error.set('Select an adviser.');
      return;
    }

    const body: { name: string; email: string; password: string; adviserId?: string } = {
      name: name.trim(),
      email: email.trim(),
      password,
    };
    if (this.isTenantAdmin) {
      body.adviserId = this.adviserId || adviserId;
    }

    this.api.create(body).subscribe((created) => {
      void this.router.navigate(['/customers', created.id]);
    });
  }
}
