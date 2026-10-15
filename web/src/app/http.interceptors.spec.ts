import { HttpClient, HttpErrorResponse, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AuthService } from './auth.service';
import { authInterceptor, describeProblem, problemInterceptor } from './http.interceptors';
import { NoticeService } from './notice.service';

describe('HTTP interceptors', () => {
  let http: HttpClient;
  let controller: HttpTestingController;
  let token: string | null;
  const signOut = jasmine.createSpy('signOut');

  beforeEach(() => {
    token = 'abc';
    signOut.calls.reset();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(withInterceptors([authInterceptor, problemInterceptor])),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: { token: () => token, signOut } },
      ],
    });
    http = TestBed.inject(HttpClient);
    controller = TestBed.inject(HttpTestingController);
  });

  it('adds the bearer token to API calls', () => {
    http.get('/api/events').subscribe();
    expect(controller.expectOne('/api/events').request.headers.get('Authorization')).toBe('Bearer abc');
  });

  it('never sends the token to other URLs', () => {
    http.get('https://example.com/data').subscribe();
    expect(controller.expectOne('https://example.com/data').request.headers.has('Authorization')).toBeFalse();
  });

  it('signs out when the API answers 401', () => {
    http.get('/api/events').subscribe({ error: () => undefined });
    controller.expectOne('/api/events').flush(null, { status: 401, statusText: 'Unauthorized' });
    expect(signOut).toHaveBeenCalled();
  });

  it('shows problem details as a notice', () => {
    const notices = TestBed.inject(NoticeService);
    http.post('/api/events/x/bookings', null).subscribe({ error: () => undefined });
    controller.expectOne('/api/events/x/bookings').flush(
      { status: 409, title: 'Booking closed', detail: 'The event has started.' },
      { status: 409, statusText: 'Conflict' },
    );
    expect(notices.notices()[0].text).toBe('Booking closed: The event has started.');
  });

  it('explains an unreachable API', () => {
    expect(describeProblem(new HttpErrorResponse({ status: 0 }))).toContain('unreachable');
  });
});
