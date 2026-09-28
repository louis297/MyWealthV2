import { Component, inject, OnInit } from '@angular/core';
import { Router } from '@angular/router';
import { RUNTIME_CONFIG } from '../../shared/api/runtime-config';
import { startAuthorize } from './oidc';
import { isReturnPath } from './session.paths';
import { SessionStore } from './session.store';

@Component({
  selector: 'app-session-page',
  templateUrl: './session-page.html',
})
export class SessionPage implements OnInit {
  private readonly config = inject(RUNTIME_CONFIG);
  private readonly router = inject(Router);
  private readonly session = inject(SessionStore);

  ngOnInit(): void {
    if (this.session.accessToken()) {
      const returnTo = this.session.returnTo();
      this.session.setReturnTo(null);
      void this.router.navigateByUrl(isReturnPath(returnTo) ? returnTo : '/');
      return;
    }

    void startAuthorize(this.config.identityAuthority);
  }
}
