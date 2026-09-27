import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { MatButton } from '@angular/material/button';
import { MatFormField, MatLabel } from '@angular/material/form-field';
import { MatInput } from '@angular/material/input';
import { Adviser, AdvisersApi } from './advisers-api';

@Component({
  selector: 'app-adviser-edit-page',
  imports: [FormsModule, MatFormField, MatLabel, MatInput, MatButton],
  templateUrl: './adviser-edit-page.html',
})
export class AdviserEditPage implements OnInit {
  private readonly api = inject(AdvisersApi);
  private readonly route = inject(ActivatedRoute);

  protected readonly adviser = signal<Adviser | null>(null);
  protected readonly notFound = signal(false);

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id') ?? '';
    this.api.get(id).subscribe({
      next: (adviser) => this.adviser.set(adviser),
      error: (error: unknown) => {
        if (error instanceof HttpErrorResponse && error.status === 404) {
          this.notFound.set(true);
        }
      },
    });
  }

  submit(adviser: Adviser, name: string): void {
    this.api.update(adviser.id, { name: name.trim(), rowVersion: adviser.rowVersion }).subscribe();
  }
}
