import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { ApiService } from './api.service';
import { AuthService } from './auth.service';
import { EventsComponent, filterEvents } from './events.component';
import { LabEvent } from './models';

const event = (title: string, labTemplate: string, extra: Partial<LabEvent> = {}): LabEvent => ({
  eventId: title, title, labTemplate, startsAt: '2026-10-20T09:00:00Z', endsAt: '2026-10-20T11:00:00Z',
  capacity: 10, confirmed: 4, waitlisted: 0, seatsLeft: 6, myBookingId: null, myStatus: null, ...extra,
});

describe('EventsComponent', () => {
  let fixture: ComponentFixture<EventsComponent>;
  const events = [event('AKS bootcamp', 'aks-fundamentals'), event('Key Vault rotation', 'keyvault', { seatsLeft: 0, confirmed: 10 })];

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [EventsComponent],
      providers: [
        provideRouter([]),
        { provide: ApiService, useValue: { events: () => of(events) } },
        { provide: AuthService, useValue: { isInstructor: () => false } },
      ],
    });
  });

  it('filters by title or template after the debounce', fakeAsync(() => {
    fixture = TestBed.createComponent(EventsComponent);
    fixture.detectChanges();
    tick(250);
    expect(fixture.componentInstance.events()?.length).toBe(2);

    fixture.componentInstance.search.setValue('vault');
    tick(100);
    expect(fixture.componentInstance.events()?.length).toBe(2); // still debouncing
    tick(150);
    expect(fixture.componentInstance.events()?.map((e) => e.title)).toEqual(['Key Vault rotation']);

    fixture.destroy(); // stops the polling timer
  }));

  it('offers the waitlist when an event is full', fakeAsync(() => {
    fixture = TestBed.createComponent(EventsComponent);
    fixture.detectChanges();
    tick(250);
    fixture.detectChanges();
    const buttons = Array.from(fixture.nativeElement.querySelectorAll('.actions button') as NodeListOf<HTMLButtonElement>);
    expect(buttons.map((b) => b.textContent?.trim())).toEqual(['Book a seat', 'Join waitlist']);
    fixture.destroy();
  }));

  it('filterEvents matches case-insensitively', () => {
    expect(filterEvents(events, 'aks').length).toBe(1);
    expect(filterEvents(events, '').length).toBe(2);
  });
});
