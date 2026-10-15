import { DatePipe } from '@angular/common';
import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import {
  BehaviorSubject, Subject, catchError, combineLatest, debounceTime, distinctUntilChanged,
  map, merge, of, startWith, switchMap, timer,
} from 'rxjs';
import { ApiService } from './api.service';
import { AuthService } from './auth.service';
import { EventFormComponent } from './event-form.component';
import { LabEvent } from './models';
import { NoticeService } from './notice.service';

@Component({
  selector: 'app-events',
  standalone: true,
  imports: [DatePipe, ReactiveFormsModule, RouterLink, EventFormComponent],
  template: `
    <header class="page-head">
      <div>
        <h1>Upcoming lab events</h1>
        <p class="muted">Updates every 15 seconds. Seat counts come straight from the stored procedures.</p>
      </div>
      <input class="search" type="search" placeholder="Filter by title or lab" [formControl]="search" aria-label="Filter events" />
    </header>

    @if (auth.isInstructor()) {
      <app-event-form (created)="refresh$.next()" />
    }

    @if (events(); as list) {
      @if (list.length === 0) {
        <p class="empty">No events match.</p>
      }
      <ul class="events">
        @for (e of list; track e.eventId) {
          <li class="event">
            <div class="event-main">
              <p class="when">{{ e.startsAt | date: 'EEE d MMM, HH:mm' }} – {{ e.endsAt | date: 'HH:mm' }}</p>
              <h2>{{ e.title }}</h2>
              <p class="muted mono">{{ e.labTemplate }}</p>
            </div>
            <div class="seats" [attr.aria-label]="e.confirmed + ' of ' + e.capacity + ' seats taken'">
              <div class="bar"><span [style.width.%]="(e.confirmed / e.capacity) * 100"></span></div>
              <p>
                <strong>{{ e.seatsLeft }}</strong> of {{ e.capacity }} seats left
                @if (e.waitlisted > 0) { · {{ e.waitlisted }} waiting }
              </p>
            </div>
            <div class="actions">
              @if (auth.isInstructor()) {
                <a class="button" [routerLink]="['/events', e.eventId, 'roster']">Roster</a>
              } @else if (e.myStatus) {
                <span class="status" [class.waitlisted]="e.myStatus === 'waitlisted'">{{ e.myStatus === 'confirmed' ? 'Seat confirmed' : 'On the waitlist' }}</span>
                <button type="button" class="ghost" (click)="cancel(e)" [disabled]="pending() === e.eventId">Cancel</button>
              } @else {
                <button type="button" (click)="book(e)" [disabled]="pending() === e.eventId">
                  {{ e.seatsLeft > 0 ? 'Book a seat' : 'Join waitlist' }}
                </button>
              }
            </div>
          </li>
        }
      </ul>
    } @else {
      <p class="muted">Loading events…</p>
    }
  `,
})
export class EventsComponent {
  private readonly api = inject(ApiService);
  private readonly notices = inject(NoticeService);
  readonly auth = inject(AuthService);

  readonly search = new FormControl('', { nonNullable: true });
  readonly refresh$ = new Subject<void>();
  readonly pending = signal<string | null>(null);
  readonly events = signal<LabEvent[] | null>(null);
  private readonly all$ = new BehaviorSubject<LabEvent[] | null>(null);

  constructor() {
    const destroyRef = inject(DestroyRef);

    // Poll every 15 s and on demand; switchMap drops stale responses when a newer request starts.
    merge(timer(0, 15_000), this.refresh$)
      .pipe(
        switchMap(() => this.api.events().pipe(catchError(() => of(null)))),
        takeUntilDestroyed(destroyRef),
      )
      .subscribe((list) => {
        if (list) this.all$.next(list);
      });

    const term$ = this.search.valueChanges.pipe(
      startWith(this.search.value),
      debounceTime(200),
      map((v) => v.trim().toLowerCase()),
      distinctUntilChanged(),
    );

    combineLatest([this.all$, term$])
      .pipe(
        map(([list, term]) => list && filterEvents(list, term)),
        takeUntilDestroyed(destroyRef),
      )
      .subscribe((list) => this.events.set(list));
  }

  book(e: LabEvent): void {
    this.pending.set(e.eventId);
    this.api.book(e.eventId, crypto.randomUUID()).subscribe({
      next: (booking) => {
        this.notices.ok(
          booking.status === 'confirmed'
            ? `Seat confirmed for ${booking.eventTitle}.`
            : `You are number ${booking.queuePosition} on the waitlist for ${booking.eventTitle}.`,
        );
        this.done();
      },
      error: () => this.done(),
    });
  }

  cancel(e: LabEvent): void {
    if (!e.myBookingId) return;
    this.pending.set(e.eventId);
    this.api.cancel(e.myBookingId).subscribe({
      next: (res) => {
        this.notices.ok(res.promotedBookingId ? 'Cancelled. Your seat went to the next person on the waitlist.' : 'Booking cancelled.');
        this.done();
      },
      error: () => this.done(),
    });
  }

  private done(): void {
    this.pending.set(null);
    this.refresh$.next();
  }
}

export function filterEvents(list: LabEvent[], term: string): LabEvent[] {
  if (!term) return list;
  return list.filter((e) => e.title.toLowerCase().includes(term) || e.labTemplate.toLowerCase().includes(term));
}
