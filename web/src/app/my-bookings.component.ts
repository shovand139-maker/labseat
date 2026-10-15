import { DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { ApiService } from './api.service';
import { Booking } from './models';
import { NoticeService } from './notice.service';

@Component({
  selector: 'app-my-bookings',
  standalone: true,
  imports: [DatePipe],
  template: `
    <header class="page-head"><h1>My bookings</h1></header>
    <table class="roster">
      <thead><tr><th>Event</th><th>Starts</th><th>Status</th><th></th></tr></thead>
      <tbody>
        @for (b of bookings(); track b.bookingId) {
          <tr>
            <td>{{ b.eventTitle }}</td>
            <td>{{ b.startsAt | date: 'EEE d MMM, HH:mm' }}</td>
            <td>
              @switch (b.status) {
                @case ('confirmed') { Confirmed }
                @case ('waitlisted') { Waitlist #{{ b.queuePosition }} }
                @default { Cancelled }
              }
            </td>
            <td>
              @if (b.status !== 'cancelled') {
                <button type="button" class="ghost" (click)="cancel(b)">Cancel</button>
              }
            </td>
          </tr>
        } @empty {
          <tr><td colspan="4" class="muted">You have not booked anything yet.</td></tr>
        }
      </tbody>
    </table>
  `,
})
export class MyBookingsComponent {
  private readonly api = inject(ApiService);
  private readonly notices = inject(NoticeService);
  readonly bookings = signal<Booking[]>([]);

  constructor() {
    this.load();
  }

  cancel(b: Booking): void {
    this.api.cancel(b.bookingId).subscribe(() => {
      this.notices.ok('Booking cancelled.');
      this.load();
    });
  }

  private load(): void {
    this.api.myBookings().subscribe((list) => this.bookings.set(list));
  }
}
