import { FormControl, FormGroup } from '@angular/forms';
import { eventWindowValidator, futureValidator } from './validators';

describe('validators', () => {
  const group = (startsAt: string, endsAt: string) =>
    new FormGroup({ startsAt: new FormControl(startsAt), endsAt: new FormControl(endsAt) }, { validators: eventWindowValidator(12) });

  it('accepts a normal window', () => {
    expect(group('2026-10-20T09:00', '2026-10-20T12:00').errors).toBeNull();
  });

  it('rejects an end before the start', () => {
    expect(group('2026-10-20T09:00', '2026-10-20T08:00').errors?.['window']).toContain('end after');
  });

  it('rejects events longer than the limit', () => {
    expect(group('2026-10-20T09:00', '2026-10-20T22:00').errors?.['window']).toContain('12 hours');
  });

  it('flags times in the past', () => {
    const now = () => Date.parse('2026-10-20T09:00:00');
    expect(new FormControl('2026-10-20T08:00', futureValidator(now)).errors?.['past']).toBeTruthy();
    expect(new FormControl('2026-10-20T10:00', futureValidator(now)).errors).toBeNull();
  });
});
