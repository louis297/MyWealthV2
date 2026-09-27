import { Component, inject } from '@angular/core';
import { SessionStore } from './session.store';

@Component({
  selector: 'app-forbidden-page',
  templateUrl: './forbidden-page.html',
})
export class ForbiddenPage {
  protected readonly session = inject(SessionStore);
}
