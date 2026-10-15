import { Component, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from './auth.service';
import { NoticeService } from './notice.service';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  template: `
    <header class="topbar">
      <a routerLink="/events" class="brand">LabSeat</a>
      @if (auth.user(); as user) {
        <nav>
          <a routerLink="/events" routerLinkActive="active">Events</a>
          @if (!auth.isInstructor()) {
            <a routerLink="/my-bookings" routerLinkActive="active">My bookings</a>
          }
        </nav>
        <span class="who">{{ user.displayName }} · {{ user.tenantName }} · {{ user.role }}</span>
        <button type="button" class="ghost" (click)="auth.signOut()">Sign out</button>
      }
    </header>
    <main><router-outlet /></main>
    <div class="notices" role="status" aria-live="polite">
      @for (n of notices.notices(); track n.id) {
        <p class="notice" [class.error]="n.kind === 'error'" (click)="notices.dismiss(n.id)">{{ n.text }}</p>
      }
    </div>
  `,
})
export class AppComponent {
  readonly auth = inject(AuthService);
  readonly notices = inject(NoticeService);
}
