import { Component, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { catchError, of } from 'rxjs';
import { ApiService } from './api.service';
import { AuthService } from './auth.service';
import { DemoUser } from './models';

@Component({
  selector: 'app-sign-in',
  standalone: true,
  template: `
    <section class="sign-in">
      <div class="intro">
        <p class="eyebrow">Instructor-led lab events</p>
        <h1>Book a seat. Join the waitlist. Never overbook.</h1>
        <p class="lede">
          Seats are allocated by SQL Server stored procedures that lock the event row, so forty people
          clicking at once still fill exactly the seats there are. The rest wait in order and move up
          automatically when someone cancels.
        </p>
      </div>
      <div class="card">
        <h2>Sign in as a demo user</h2>
        @if (users(); as list) {
          @for (group of groups(list); track group.tenant) {
            <h3>{{ group.tenant }}</h3>
            <ul class="people">
              @for (user of group.users; track user.userId) {
                <li>
                  <button type="button" (click)="signIn(user)" [disabled]="busy === user.email">
                    <span class="name">{{ user.displayName }}</span>
                    <span class="role" [class.instructor]="user.role === 'instructor'">{{ user.role }}</span>
                  </button>
                </li>
              }
            </ul>
          }
        } @else {
          <p class="muted">Loading demo users from the API…</p>
        }
      </div>
    </section>
  `,
})
export class SignInComponent {
  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  busy: string | null = null;

  readonly users = toSignal(this.api.demoUsers().pipe(catchError(() => of([] as DemoUser[]))));

  groups(users: DemoUser[]): { tenant: string; users: DemoUser[] }[] {
    const byTenant = new Map<string, DemoUser[]>();
    for (const user of users) {
      byTenant.set(user.tenantName, [...(byTenant.get(user.tenantName) ?? []), user]);
    }
    return [...byTenant].map(([tenant, list]) => ({ tenant, users: list }));
  }

  signIn(user: DemoUser): void {
    this.busy = user.email;
    this.auth
      .signIn(user.email)
      .subscribe({
        next: () => void this.router.navigateByUrl('/events'),
        error: () => (this.busy = null),
      });
  }
}
