import { DatePipe } from '@angular/common';
import { Component, Input, OnInit, inject, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { forkJoin } from 'rxjs';
import { ApiService } from './api.service';
import { LabEvent, RosterEntry } from './models';
import { NoticeService } from './notice.service';

@Component({
  selector: 'app-roster',
  standalone: true,
  imports: [DatePipe, ReactiveFormsModule, RouterLink],
  template: `
    <a routerLink="/events" class="back">← All events</a>
    @if (event(); as e) {
      <header class="page-head">
        <div>
          <h1>{{ e.title }}</h1>
          <p class="muted">{{ e.startsAt | date: 'EEE d MMM, HH:mm' }} · {{ e.confirmed }} confirmed · {{ e.waitlisted }} waiting</p>
        </div>
        <form class="capacity" (ngSubmit)="saveCapacity(e)">
          <label>Seats <input type="number" [formControl]="capacity" min="1" max="500" /></label>
          <button type="submit" [disabled]="capacity.invalid || capacity.value === e.capacity">Update</button>
        </form>
      </header>
      <table class="roster">
        <thead><tr><th>Learner</th><th>Status</th><th>Booked</th><th></th></tr></thead>
        <tbody>
          @for (r of roster(); track r.bookingId) {
            <tr>
              <td><strong>{{ r.displayName }}</strong><br /><span class="muted">{{ r.email }}</span></td>
              <td>{{ r.status === 'confirmed' ? 'Confirmed' : 'Waitlist #' + r.queuePosition }}</td>
              <td>{{ r.createdAt | date: 'd MMM, HH:mm:ss' }}</td>
              <td><button type="button" class="ghost" (click)="remove(r)">Remove</button></td>
            </tr>
          } @empty {
            <tr><td colspan="4" class="muted">No bookings yet.</td></tr>
          }
        </tbody>
      </table>
    }
  `,
})
export class RosterComponent implements OnInit {
  @Input({ required: true }) eventId!: string;
  private readonly api = inject(ApiService);
  private readonly notices = inject(NoticeService);

  readonly event = signal<LabEvent | null>(null);
  readonly roster = signal<RosterEntry[]>([]);
  readonly capacity = new FormControl(1, { nonNullable: true, validators: [Validators.required, Validators.min(1), Validators.max(500)] });

  ngOnInit(): void {
    this.load();
  }

  saveCapacity(e: LabEvent): void {
    this.api.setCapacity(e.eventId, this.capacity.value).subscribe(() => {
      this.notices.ok('Capacity updated. Freed seats went to the waitlist in order.');
      this.load();
    });
  }

  remove(entry: RosterEntry): void {
    this.api.cancel(entry.bookingId).subscribe(() => {
      this.notices.ok(`Removed ${entry.displayName}.`);
      this.load();
    });
  }

  private load(): void {
    forkJoin({ event: this.api.event(this.eventId), roster: this.api.roster(this.eventId) }).subscribe(({ event, roster }) => {
      this.event.set(event);
      this.roster.set(roster);
      this.capacity.setValue(event.capacity);
    });
  }
}
