import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, OnInit, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Adviser, AdvisersApi } from './advisers-api';

const assignedCustomersMessage =
  'Reassign or disable assigned customers before disabling this adviser.';

@Component({
  selector: 'app-adviser-detail-page',
  imports: [RouterLink],
  templateUrl: './adviser-detail-page.html',
})
export class AdviserDetailPage implements OnInit {
  private readonly api = inject(AdvisersApi);
  private readonly route = inject(ActivatedRoute);

  protected readonly adviser = signal<Adviser | null>(null);
  protected readonly notFound = signal(false);
  protected readonly conflict = signal(false);
  protected readonly guardMessage = signal<string | null>(null);

  ngOnInit(): void {
    this.load(this.route.snapshot.paramMap.get('id') ?? '');
  }

  changeStatus(adviser: Adviser): void {
    const action = adviser.status === 'disabled' ? 'enable' : 'disable';
    if (action === 'disable' && !window.confirm('Disable this adviser?')) {
      return;
    }

    this.api.changeStatus(adviser.id, action, adviser.rowVersion).subscribe({
      error: (error: unknown) => {
        if (!(error instanceof HttpErrorResponse)) {
          return;
        }
        if (error.status === 409) {
          this.conflict.set(true);
          return;
        }
        const messages = validationMessages(error.error);
        if (messages.includes(assignedCustomersMessage)) {
          this.guardMessage.set(assignedCustomersMessage);
        }
      },
    });
  }

  private load(id: string): void {
    this.api.get(id).subscribe({
      next: (adviser) => this.adviser.set(adviser),
      error: (error: unknown) => {
        if (error instanceof HttpErrorResponse && error.status === 404) {
          this.notFound.set(true);
        }
      },
    });
  }
}

function validationMessages(body: unknown): string[] {
  if (!body || typeof body !== 'object' || !('errors' in body)) {
    return [];
  }
  const errors = (body as { errors?: Record<string, string[]> }).errors;
  if (!errors) {
    return [];
  }
  return Object.values(errors).flat();
}
