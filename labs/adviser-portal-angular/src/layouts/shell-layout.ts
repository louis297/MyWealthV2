import { Component, inject, OnInit } from '@angular/core';
import { Router, RouterLink, RouterOutlet } from '@angular/router';
import { MatSidenav, MatSidenavContainer, MatSidenavContent } from '@angular/material/sidenav';
import { MatToolbar } from '@angular/material/toolbar';
import { PeopleApi } from '../shared/api/people-api';
import { RUNTIME_CONFIG } from '../shared/api/runtime-config';
import { startEndSession } from '../features/session/oidc';
import { canAccessAdvisers, canAccessCustomers, canAccessProfile, roles } from '../features/session/roles';
import { SessionStore } from '../features/session/session.store';

@Component({
  selector: 'app-shell-layout',
  imports: [RouterOutlet, RouterLink, MatToolbar, MatSidenavContainer, MatSidenav, MatSidenavContent],
  templateUrl: './shell-layout.html',
})
export class ShellLayout implements OnInit {
  private readonly router = inject(Router);
  private readonly people = inject(PeopleApi);
  private readonly config = inject(RUNTIME_CONFIG);
  protected readonly session = inject(SessionStore);
  protected readonly canAccessCustomers = canAccessCustomers;
  protected readonly canAccessAdvisers = canAccessAdvisers;
  protected readonly canAccessProfile = canAccessProfile;

  ngOnInit(): void {
    if (!this.session.accessToken() || this.session.currentUser()) {
      return;
    }

    this.people.me().subscribe((user) => {
      this.session.setCurrentUser(user);
      this.land(user.role);
      if (!user.tenantCode || user.role === roles.customer) {
        return;
      }
      this.people.tenantByCode(user.tenantCode).subscribe((tenant) => {
        this.session.setTenantName(tenant.name);
      });
    });
  }

  private land(role: string): void {
    const path = this.router.url.split('?')[0] || '/';
    if (role === roles.customer) {
      if (path !== '/forbidden') {
        void this.router.navigate(['/forbidden']);
      }
      return;
    }

    if (path === '/') {
      void this.router.navigate(role === roles.systemAdmin ? ['/profile'] : ['/customers']);
      return;
    }

    const allowed =
      path === '/forbidden' ||
      (path === '/profile' && canAccessProfile(role)) ||
      (path.startsWith('/customers') && canAccessCustomers(role)) ||
      (path.startsWith('/advisers') && canAccessAdvisers(role));
    if (!allowed) {
      void this.router.navigate(['/forbidden']);
    }
  }

  signOut(): void {
    void startEndSession(this.session, this.config.identityAuthority);
  }
}
