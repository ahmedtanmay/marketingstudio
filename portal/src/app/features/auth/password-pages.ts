import { ChangeDetectionStrategy, Component, inject, input, signal } from '@angular/core';
import { AbstractControl, NonNullableFormBuilder, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { AuthApi } from '../../core/auth/auth-api';
import { AuthLayout } from './auth-layout';
import { FormState } from './form-state';

const formImports = [AuthLayout, ReactiveFormsModule, RouterLink, MatButtonModule, MatFormFieldModule, MatInputModule, MatProgressBarModule];

/** AUTH-4: request a reset link. The response is the same whether or not the email exists. */
@Component({
  selector: 'ms-forgot-password',
  imports: formImports,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ms-auth-layout heading="Reset your password">
      @if (sent()) {
        <p role="status" data-testid="sent">{{ sent() }}</p>
        <a mat-stroked-button routerLink="/login">Back to sign in</a>
      } @else {
        <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
          <p>Enter your email and we'll send you a link to choose a new password.</p>
          @if (state.error()) {
            <p class="form-error" role="alert">{{ state.error() }}</p>
          }
          <mat-form-field appearance="outline">
            <mat-label>Email</mat-label>
            <input matInput type="email" formControlName="email" autocomplete="email" required />
            @if (form.controls.email.invalid) {
              <mat-error>Enter a valid email</mat-error>
            }
          </mat-form-field>
          <button mat-flat-button type="submit" [disabled]="state.busy()">Send reset link</button>
          @if (state.busy()) {
            <mat-progress-bar mode="indeterminate" />
          }
        </form>
      }
      <p footer><a routerLink="/login">Back to sign in</a></p>
    </ms-auth-layout>
  `,
})
export class ForgotPassword {
  private readonly api = inject(AuthApi);
  protected readonly state = new FormState();
  protected readonly sent = signal<string | null>(null);
  protected readonly form = inject(NonNullableFormBuilder).group({ email: ['', [Validators.required, Validators.email]] });

  protected async submit(): Promise<void> {
    await this.state.submit(this.form, async () => {
      const response = await firstValueFrom(this.api.forgotPassword(this.form.getRawValue().email));
      this.sent.set(response.message);
    });
  }
}

const passwordsMatch = (group: AbstractControl): ValidationErrors | null =>
  group.get('newPassword')?.value === group.get('confirm')?.value ? null : { mismatch: true };

/** AUTH-4: set a new password from the emailed link (?email=…&token=…). */
@Component({
  selector: 'ms-reset-password',
  imports: formImports,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ms-auth-layout heading="Choose a new password">
      @if (done()) {
        <p role="status">Your password has been changed and you've been signed out everywhere.</p>
        <a mat-flat-button routerLink="/login">Sign in</a>
      } @else if (!email() || !token()) {
        <p class="form-error" role="alert">This reset link is incomplete. Request a new one.</p>
        <a mat-stroked-button routerLink="/forgot-password">Request a new link</a>
      } @else {
        <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
          @if (state.error()) {
            <p class="form-error" role="alert">{{ state.error() }}</p>
          }
          @for (message of fieldMessages(); track message) {
            <p class="form-error" role="alert">{{ message }}</p>
          }
          <mat-form-field appearance="outline">
            <mat-label>New password</mat-label>
            <input matInput type="password" formControlName="newPassword" autocomplete="new-password" required />
            <mat-hint>At least 10 characters</mat-hint>
            @if (form.controls.newPassword.invalid) {
              <mat-error>Use at least 10 characters</mat-error>
            }
          </mat-form-field>
          <mat-form-field appearance="outline">
            <mat-label>Confirm new password</mat-label>
            <input matInput type="password" formControlName="confirm" autocomplete="new-password" required />
          </mat-form-field>
          @if (form.hasError('mismatch') && form.controls.confirm.touched) {
            <p class="form-error" role="alert">The passwords don't match.</p>
          }
          <button mat-flat-button type="submit" [disabled]="state.busy()">Change password</button>
          @if (state.busy()) {
            <mat-progress-bar mode="indeterminate" />
          }
        </form>
      }
    </ms-auth-layout>
  `,
})
export class ResetPassword {
  readonly email = input<string>();
  readonly token = input<string>();

  private readonly api = inject(AuthApi);
  protected readonly state = new FormState();
  protected readonly done = signal(false);
  protected readonly form = inject(NonNullableFormBuilder).group(
    {
      newPassword: ['', [Validators.required, Validators.minLength(10), Validators.maxLength(128)]],
      confirm: ['', Validators.required],
    },
    { validators: passwordsMatch },
  );

  protected fieldMessages(): string[] {
    return Object.values(this.state.fields());
  }

  protected async submit(): Promise<void> {
    const ok = await this.state.submit(this.form, () =>
      firstValueFrom(this.api.resetPassword(this.email()!, this.token()!, this.form.getRawValue().newPassword)),
    );
    this.done.set(ok);
  }
}
