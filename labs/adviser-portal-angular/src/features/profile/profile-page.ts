import { Component, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { MatButton } from '@angular/material/button';
import { MatFormField, MatLabel } from '@angular/material/form-field';
import { MatInput } from '@angular/material/input';
import { PeopleApi } from '../../shared/api/people-api';
import { SessionStore } from '../session/session.store';

@Component({
  selector: 'app-profile-page',
  imports: [FormsModule, MatFormField, MatLabel, MatInput, MatButton],
  templateUrl: './profile-page.html',
})
export class ProfilePage {
  private readonly people = inject(PeopleApi);
  private readonly router = inject(Router);
  protected readonly session = inject(SessionStore);

  saveName(name: string): void {
    const user = this.session.currentUser();
    if (!user) {
      return;
    }

    this.people.updateMe({ name: name.trim(), rowVersion: user.rowVersion }).subscribe();
  }

  savePassword(currentPassword: string, newPassword: string): void {
    this.people.updatePassword({ currentPassword, newPassword }).subscribe(() => {
      this.session.clear();
      void this.router.navigate(['/session']);
    });
  }
}
