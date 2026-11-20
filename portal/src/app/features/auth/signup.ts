import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { Router, RouterLink } from '@angular/router';
import { AuthStore } from '../../core/auth/auth.store';
import { AuthLayout } from './auth-layout';
import { FormState } from './form-state';

/** AUTH-1: sign up — creates the workspace with a 14-day trial, then signs in. */
@Component({
  selector: 'ms-signup',
  imports: [AuthLayout, ReactiveFormsModule, RouterLink, MatButtonModule, MatFormFieldModule, MatInputModule, MatProgressBarModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ms-auth-layout heading="Start your free trial" subheading="14 days, no card required">
      <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
        @if (state.error()) {
          <p class="form-error" role="alert">{{ state.error() }}</p>
        }
        <mat-form-field appearance="outline">
          <mat-label>Your name</mat-label>
          <input matInput formControlName="name" autocomplete="name" required data-testid="name" />
          @if (form.controls.name.hasError('required')) {
            <mat-error>Enter your name</mat-error>
          }
        </mat-form-field>
        <mat-form-field appearance="outline">
          <mat-label>Company name</mat-label>
          <input matInput formControlName="companyName" autocomplete="organization" required data-testid="company" />
          @if (form.controls.companyName.hasError('required')) {
            <mat-error>Enter your company name</mat-error>
          }
        </mat-form-field>
        <mat-form-field appearance="outline">
          <mat-label>Work email</mat-label>
          <input matInput type="email" formControlName="email" autocomplete="email" required data-testid="email" />
          @if (form.controls.email.invalid) {
            <mat-error>Enter a valid email</mat-error>
          }
        </mat-form-field>
        <mat-form-field appearance="outline">
          <mat-label>Password</mat-label>
          <input matInput type="password" formControlName="password" autocomplete="new-password" required data-testid="password" />
          <mat-hint>At least 10 characters. Avoid common passwords.</mat-hint>
          @if (form.controls.password.hasError('minlength') || form.controls.password.hasError('required')) {
            <mat-error>Use at least 10 characters</mat-error>
          }
        </mat-form-field>
        @if (state.fields()['password']) {
          <p class="form-error" role="alert">{{ state.fields()['password'] }}</p>
        }
        <button mat-flat-button type="submit" [disabled]="state.busy()" data-testid="submit">Create workspace</button>
        @if (state.busy()) {
          <mat-progress-bar mode="indeterminate" />
        }
      </form>
      <p footer>Already have an account? <a routerLink="/login">Sign in</a></p>
    </ms-auth-layout>
  `,
})
export class Signup {
  private readonly store = inject(AuthStore);
  private readonly router = inject(Router);
  protected readonly state = new FormState();
  protected readonly form = inject(NonNullableFormBuilder).group({
    name: ['', [Validators.required, Validators.maxLength(100)]],
    companyName: ['', [Validators.required, Validators.maxLength(120)]],
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required, Validators.minLength(10), Validators.maxLength(128)]],
  });

  protected async submit(): Promise<void> {
    if (await this.state.submit(this.form, () => this.store.signUp(this.form.getRawValue()))) {
      await this.router.navigateByUrl('/dashboard');
      return;
    }

    // Registration never reveals whether an email exists, so a failed sign-in right after it usually
    // means the address already has an account (with a different password).
    if (this.state.lastProblem()?.status === 401) {
      this.state.error.set(
        "We couldn't create a workspace with these details. If you already have an account, sign in or reset your password.",
      );
    }
  }
}
