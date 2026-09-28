import { Component, inject, OnInit, signal } from '@angular/core';
import { Router } from '@angular/router';
import { RUNTIME_CONFIG } from '../../shared/api/runtime-config';
import { completeCallback } from './oidc';
import { SessionStore } from './session.store';

@Component({
  selector: 'app-callback-page',
  templateUrl: './callback-page.html',
})
export class CallbackPage implements OnInit {
  private readonly session = inject(SessionStore);
  private readonly router = inject(Router);
  private readonly config = inject(RUNTIME_CONFIG);
  protected readonly error = signal<string | null>(null);

  ngOnInit(): void {
    void completeCallback(this.session, this.config.identityAuthority)
      .then((path) => this.router.navigateByUrl(path))
      .catch((reason: unknown) => {
        this.error.set(reason instanceof Error ? reason.message : 'Sign-in failed.');
      });
  }
}
