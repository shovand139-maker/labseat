import { Component, EventEmitter, Output, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ApiService } from './api.service';
import { Problem } from './models';
import { NoticeService } from './notice.service';
import { eventWindowValidator, futureValidator, isoToLocalInput, localInputToIso } from './validators';

@Component({
  selector: 'app-event-form',
  standalone: true,
  imports: [ReactiveFormsModule],
  template: `
    <details class="card create" [open]="open()" (toggle)="open.set($any($event.target).open)">
      <summary>Schedule a new event</summary>
      <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
        <label>Title <input formControlName="title" maxlength="120" /></label>
        <label>Lab template <input formControlName="labTemplate" placeholder="aks-fundamentals" maxlength="80" /></label>
        <label>Starts <input type="datetime-local" formControlName="startsAt" /></label>
        <label>Ends <input type="datetime-local" formControlName="endsAt" /></label>
        <label>Seats <input type="number" formControlName="capacity" min="1" max="500" /></label>
        @if (form.errors?.['window']; as message) { <p class="field-error">{{ message }}</p> }
        @if (form.controls.startsAt.errors?.['past']; as message) { <p class="field-error">{{ message }}</p> }
        @for (message of serverErrors(); track message) { <p class="field-error">{{ message }}</p> }
        <button type="submit" [disabled]="form.invalid || saving()">Create event</button>
      </form>
    </details>
  `,
})
export class EventFormComponent {
  @Output() readonly created = new EventEmitter<void>();
  private readonly api = inject(ApiService);
  private readonly notices = inject(NoticeService);

  readonly open = signal(false);
  readonly saving = signal(false);
  readonly serverErrors = signal<string[]>([]);

  private readonly start = new Date(Date.now() + 24 * 3_600_000);
  readonly form = inject(FormBuilder).nonNullable.group(
    {
      title: ['', [Validators.required, Validators.minLength(3), Validators.maxLength(120)]],
      labTemplate: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(80)]],
      startsAt: [isoToLocalInput(this.start), [Validators.required, futureValidator()]],
      endsAt: [isoToLocalInput(new Date(this.start.getTime() + 2 * 3_600_000)), Validators.required],
      capacity: [20, [Validators.required, Validators.min(1), Validators.max(500)]],
    },
    { validators: eventWindowValidator(12) },
  );

  submit(): void {
    if (this.form.invalid) return;
    const v = this.form.getRawValue();
    this.saving.set(true);
    this.serverErrors.set([]);
    this.api
      .createEvent({ ...v, startsAt: localInputToIso(v.startsAt), endsAt: localInputToIso(v.endsAt) })
      .subscribe({
        next: (event) => {
          this.notices.ok(`Scheduled “${event.title}”.`);
          this.form.controls.title.reset('');
          this.saving.set(false);
          this.created.emit();
        },
        error: (err: HttpErrorResponse) => {
          const problem = err.error as Problem | null;
          this.serverErrors.set(Object.values(problem?.errors ?? {}).flat());
          this.saving.set(false);
        },
      });
  }
}
