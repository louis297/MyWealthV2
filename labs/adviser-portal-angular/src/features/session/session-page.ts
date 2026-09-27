import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButton } from '@angular/material/button';
import { MatFormField, MatLabel } from '@angular/material/form-field';
import { MatInput } from '@angular/material/input';
import { Router } from '@angular/router';
import { PeopleApi } from '../../shared/api/people-api';
import { isInAppPath } from './session.paths';
import { SessionStore } from './session.store';

@Component({
  selector: 'app-session-page',
  imports: [FormsModule, MatFormField, MatLabel, MatInput, MatButton],
  templateUrl: './session-page.html',
})
export class SessionPage {
  private readonly session = inject(SessionStore);
  private readonly people = inject(PeopleApi);
  private readonly router = inject(Router);

  accessToken = '';
  refreshToken = '';
  readonly error = signal<string | null>(null);

  submit(accessTokenValue: string, refreshTokenValue: string): void {
    const accessToken = accessTokenValue.trim();
    if (!accessToken) {
      return;
    }

    this.error.set(null);
    this.accessToken = accessToken;
    const refreshToken = refreshTokenValue.trim();
    this.refreshToken = refreshToken;
    this.session.setTokens(accessToken, refreshToken ? refreshToken : null);
    this.people.me().subscribe({
      next: (user) => {
        this.session.setCurrentUser(user);
        if (!user.tenantCode) {
          this.continue();
          return;
        }

        this.people.tenantByCode(user.tenantCode).subscribe({
          next: (tenant) => {
            this.session.setTenantName(tenant.name);
            this.continue();
          },
          error: () => this.reject(),
        });
      },
      error: (error: unknown) => {
        if (error instanceof HttpErrorResponse && (error.status === 401 || error.status === 403)) {
          this.reject();
          return;
        }
        this.error.set('Could not load session.');
      },
    });
  }

  private continue(): void {
    const returnTo = this.session.returnTo();
    this.session.setReturnTo(null);
    void this.router.navigateByUrl(isInAppPath(returnTo) && returnTo !== '/session' ? returnTo : '/');
  }

  private reject(): void {
    this.session.clear();
    this.error.set('That token was not accepted.');
  }
}
