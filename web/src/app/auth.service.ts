import { Injectable, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { Observable, tap } from 'rxjs';
import { ApiService } from './api.service';
import { DemoUser, TokenResponse } from './models';

interface Session {
  token: string;
  expiresAt: number;
  user: DemoUser;
}

const STORAGE_KEY = 'labseat.session';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  private readonly session = signal<Session | null>(AuthService.restore());

  readonly user = computed(() => this.session()?.user ?? null);
  readonly isInstructor = computed(() => this.user()?.role === 'instructor');

  token(): string | null {
    const s = this.session();
    return s && s.expiresAt > Date.now() ? s.token : null;
  }

  signIn(email: string): Observable<TokenResponse> {
    return this.api.token(email).pipe(
      tap((res) => {
        const session: Session = { token: res.accessToken, expiresAt: Date.now() + res.expiresIn * 1000, user: res.user };
        this.session.set(session);
        try {
          sessionStorage.setItem(STORAGE_KEY, JSON.stringify(session));
        } catch {
          // storage can be unavailable (private mode); the in-memory session still works
        }
      }),
    );
  }

  signOut(): void {
    this.session.set(null);
    try {
      sessionStorage.removeItem(STORAGE_KEY);
    } catch {
      // ignore
    }
    void this.router.navigate(['/sign-in']);
  }

  private static restore(): Session | null {
    try {
      const raw = sessionStorage.getItem(STORAGE_KEY);
      const session = raw ? (JSON.parse(raw) as Session) : null;
      return session && session.expiresAt > Date.now() ? session : null;
    } catch {
      return null;
    }
  }
}
