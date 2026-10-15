import { AbstractControl, ValidationErrors, ValidatorFn } from '@angular/forms';

/** Group validator: end must be after start, and the event can last at most `maxHours`. */
export function eventWindowValidator(maxHours = 12): ValidatorFn {
  return (group: AbstractControl): ValidationErrors | null => {
    const start = Date.parse(group.get('startsAt')?.value ?? '');
    const end = Date.parse(group.get('endsAt')?.value ?? '');
    if (Number.isNaN(start) || Number.isNaN(end)) return null;
    if (end <= start) return { window: 'The event must end after it starts.' };
    if (end - start > maxHours * 3_600_000) return { window: `An event can last at most ${maxHours} hours.` };
    return null;
  };
}

export function futureValidator(now: () => number = Date.now): ValidatorFn {
  return (control: AbstractControl): ValidationErrors | null => {
    const value = Date.parse(control.value ?? '');
    return !Number.isNaN(value) && value <= now() ? { past: 'Pick a time in the future.' } : null;
  };
}

/** `datetime-local` inputs have no zone; interpret them as local time and send ISO UTC. */
export function localInputToIso(value: string): string {
  return new Date(value).toISOString();
}

export function isoToLocalInput(date: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
}
