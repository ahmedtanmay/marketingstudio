import { ChangeDetectionStrategy, Component, inject, input } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { Router, RouterLink } from '@angular/router';
import { AuthStore } from '../../core/auth/auth.store';
import { AuthLayout } from './auth-layout';
import { FormState } from './form-state';

/** AUTH-2: sign in. */
@Component({
  selector: 'ms-login',
  imports: [AuthLayout, ReactiveFormsModule, RouterLink, MatButtonModule, MatFormFieldModule, MatInputModule, MatProgressBarModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ms-auth-layout heading="Sign in" subheading="Welcome back">
      <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
        @if (state.error()) {
          <p class="form-error" role="alert" data-testid="login-error">{{ state.error() }}</p>
        }
        <mat-form-field appearance="outline">
          <mat-label>Email</mat-label>
          <input matInput type="email" formControlName="email" autocomplete="username" required data-testid="email" />
          @if (form.controls.email.hasError('required')) {
            <mat-error>Enter your email</mat-error>
          }
        </mat-form-field>
        <mat-form-field appearance="outline">
          <mat-label>Password</mat-label>
          <input matInput type="password" formControlName="password" autocomplete="current-password" required data-testid="password" />
          @if (form.controls.password.hasError('required')) {
            <mat-error>Enter your password</mat-error>
          }
        </mat-form-field>
        <a routerLink="/forgot-password" class="aside-link">Forgot password?</a>
        <button mat-flat-button type="submit" [disabled]="state.busy()" data-testid="submit">Sign in</button>
        @if (state.busy()) {
          <mat-progress-bar mode="indeterminate" />
        }
      </form>
      <p footer>New to AI Marketing Studio? <a routerLink="/signup">Start a free trial</a></p>
    </ms-auth-layout>
  `,
})
export class Login {
  /** Bound from the query string by the router (withComponentInputBinding). */
  readonly returnUrl = input<string>();

  private readonly store = inject(AuthStore);
  private readonly router = inject(Router);
  protected readonly state = new FormState();
  protected readonly form = inject(NonNullableFormBuilder).group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', Validators.required],
  });

  protected async submit(): Promise<void> {
    const { email, password } = this.form.getRawValue();
    if (await this.state.submit(this.form, () => this.store.login(email, password))) {
      const target = this.returnUrl();
      // Only same-app paths, never absolute URLs (open-redirect protection).
      await this.router.navigateByUrl(target?.startsWith('/') && !target.startsWith('//') ? target : '/dashboard');
    }
  }
}
