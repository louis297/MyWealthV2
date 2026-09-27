import { Component, inject, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Adviser, AdvisersApi } from './advisers-api';

@Component({
  selector: 'app-advisers-list-page',
  imports: [RouterLink],
  templateUrl: './advisers-list-page.html',
})
export class AdvisersListPage implements OnInit {
  private readonly api = inject(AdvisersApi);
  protected readonly advisers = signal<Adviser[]>([]);

  ngOnInit(): void {
    this.api.list().subscribe({
      next: (page) => this.advisers.set(page.items),
      error: () => this.advisers.set([]),
    });
  }
}
