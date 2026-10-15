import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ApiService } from './api.service';

describe('ApiService', () => {
  let api: ApiService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(ApiService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('sends the idempotency key when booking', () => {
    api.book('evt-1', 'key-123').subscribe();
    const req = http.expectOne('/api/events/evt-1/bookings');
    expect(req.request.method).toBe('POST');
    expect(req.request.headers.get('Idempotency-Key')).toBe('key-123');
    req.flush({});
  });

  it('encodes ids in URLs', () => {
    api.roster('a/b').subscribe();
    const req = http.expectOne('/api/events/a%2Fb/roster');
    expect(req.request.method).toBe('GET');
    req.flush([]);
  });

  it('sends the date range as ISO strings', () => {
    api.events(new Date('2026-10-01T00:00:00Z'), new Date('2026-11-01T00:00:00Z')).subscribe();
    const req = http.expectOne((r) => r.url === '/api/events');
    expect(req.request.params.get('from')).toBe('2026-10-01T00:00:00.000Z');
    expect(req.request.params.get('to')).toBe('2026-11-01T00:00:00.000Z');
    req.flush([]);
  });
});
