import { HttpClient, HttpHeaders, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { Booking, CancelResult, CreateEventRequest, DemoUser, LabEvent, RosterEntry, TokenResponse } from './models';

@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly http = inject(HttpClient);

  demoUsers(): Observable<DemoUser[]> {
    return this.http.get<DemoUser[]>('/dev/users');
  }

  token(email: string): Observable<TokenResponse> {
    return this.http.post<TokenResponse>('/dev/token', { email });
  }

  events(from?: Date, to?: Date): Observable<LabEvent[]> {
    let params = new HttpParams();
    if (from) params = params.set('from', from.toISOString());
    if (to) params = params.set('to', to.toISOString());
    return this.http.get<LabEvent[]>('/api/events', { params });
  }

  event(eventId: string): Observable<LabEvent> {
    return this.http.get<LabEvent>(`/api/events/${encodeURIComponent(eventId)}`);
  }

  createEvent(request: CreateEventRequest): Observable<LabEvent> {
    return this.http.post<LabEvent>('/api/events', request);
  }

  setCapacity(eventId: string, capacity: number): Observable<LabEvent> {
    return this.http.put<LabEvent>(`/api/events/${encodeURIComponent(eventId)}/capacity`, { capacity });
  }

  roster(eventId: string): Observable<RosterEntry[]> {
    return this.http.get<RosterEntry[]>(`/api/events/${encodeURIComponent(eventId)}/roster`);
  }

  /** The key makes retries safe: the API returns the original booking instead of creating a second one. */
  book(eventId: string, idempotencyKey: string): Observable<Booking> {
    const headers = new HttpHeaders({ 'Idempotency-Key': idempotencyKey });
    return this.http.post<Booking>(`/api/events/${encodeURIComponent(eventId)}/bookings`, null, { headers });
  }

  cancel(bookingId: string): Observable<CancelResult> {
    return this.http.delete<CancelResult>(`/api/bookings/${encodeURIComponent(bookingId)}`);
  }

  myBookings(): Observable<Booking[]> {
    return this.http.get<Booking[]>('/api/me/bookings');
  }
}
