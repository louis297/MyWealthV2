import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, OnInit, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Customer } from './customer.models';
import { CustomersApi } from './customers-api';

@Component({
  selector: 'app-customer-detail-page',
  imports: [RouterLink],
  templateUrl: './customer-detail-page.html',
})
export class CustomerDetailPage implements OnInit {
  private readonly api = inject(CustomersApi);
  private readonly route = inject(ActivatedRoute);

  protected readonly customer = signal<Customer | null>(null);
  protected readonly notFound = signal(false);
  protected readonly conflict = signal(false);

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id') ?? '';
    this.api.get(id).subscribe({
      next: (customer) => this.customer.set(customer),
      error: (error: unknown) => {
        if (error instanceof HttpErrorResponse && error.status === 404) {
          this.notFound.set(true);
        }
      },
    });
  }

  changeStatus(customer: Customer): void {
    const action = customer.status === 'disabled' ? 'enable' : 'disable';
    if (action === 'disable' && !window.confirm('Disable this customer?')) {
      return;
    }

    this.api.changeStatus(customer.id, action, customer.rowVersion).subscribe({
      error: (error: unknown) => {
        if (error instanceof HttpErrorResponse && error.status === 409) {
          this.conflict.set(true);
        }
      },
    });
  }
}
