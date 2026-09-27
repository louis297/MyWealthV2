import { Component, inject } from '@angular/core';
import { RouterLink, RouterOutlet } from '@angular/router';
import { MatSidenav, MatSidenavContainer, MatSidenavContent } from '@angular/material/sidenav';
import { MatToolbar } from '@angular/material/toolbar';
import { SessionStore } from '../features/session/session.store';
import { canAccessAdvisers, canAccessCustomers, canAccessProfile } from '../features/session/roles';

@Component({
  selector: 'app-shell-layout',
  imports: [RouterOutlet, RouterLink, MatToolbar, MatSidenavContainer, MatSidenav, MatSidenavContent],
  templateUrl: './shell-layout.html',
})
export class ShellLayout {
  protected readonly session = inject(SessionStore);
  protected readonly canAccessCustomers = canAccessCustomers;
  protected readonly canAccessAdvisers = canAccessAdvisers;
  protected readonly canAccessProfile = canAccessProfile;
}
