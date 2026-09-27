import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { MatButton } from '@angular/material/button';
import { MatFormField, MatLabel } from '@angular/material/form-field';
import { MatInput } from '@angular/material/input';
import { AdvisersApi } from './advisers-api';

@Component({
  selector: 'app-adviser-create-page',
  imports: [FormsModule, MatFormField, MatLabel, MatInput, MatButton],
  templateUrl: './adviser-create-page.html',
})
export class AdviserCreatePage {
  private readonly api = inject(AdvisersApi);
  private readonly router = inject(Router);
  protected readonly error = signal<string | null>(null);

  submit(name: string, email: string, password: string, confirm: string): void {
    if (password !== confirm) {
      this.error.set('Passwords do not match.');
      return;
    }

    this.api
      .create({ name: name.trim(), email: email.trim(), password })
      .subscribe((created) => {
        void this.router.navigate(['/advisers', created.id]);
      });
  }
}
