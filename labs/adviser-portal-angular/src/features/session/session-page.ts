import { Component, inject, OnInit } from '@angular/core';
import { RUNTIME_CONFIG } from '../../shared/api/runtime-config';
import { startAuthorize } from './oidc';

@Component({
  selector: 'app-session-page',
  templateUrl: './session-page.html',
})
export class SessionPage implements OnInit {
  private readonly config = inject(RUNTIME_CONFIG);

  ngOnInit(): void {
    void startAuthorize(this.config.identityAuthority);
  }
}
