import { signal } from '@angular/core';
import { FormGroup } from '@angular/forms';
import { Problem, fieldErrors, problemMessage, toProblem } from '../../core/api/problem';

/** Submit state shared by the auth forms: busy flag, a top-level error, and server field errors. */
export class FormState {
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly fields = signal<Record<string, string>>({});
  readonly lastProblem = signal<Problem | null>(null);

  /** Runs the action unless the form is invalid; maps API problems to messages. Returns true on success. */
  async submit(form: FormGroup, action: () => Promise<unknown>): Promise<boolean> {
    if (form.invalid) {
      form.markAllAsTouched();
      return false;
    }

    this.busy.set(true);
    this.error.set(null);
    this.fields.set({});
    try {
      await action();
      return true;
    } catch (error: unknown) {
      const problem = toProblem(error);
      this.lastProblem.set(problem);
      const fields = fieldErrors(problem);
      this.fields.set(fields);
      this.error.set(Object.keys(fields).length > 0 ? null : problemMessage(problem));
      return false;
    } finally {
      this.busy.set(false);
    }
  }
}
