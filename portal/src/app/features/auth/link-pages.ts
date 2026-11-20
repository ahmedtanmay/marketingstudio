import { ChangeDetectionStrategy, Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { InviteDetails, ROLE_LABELS } from '../../core/api/api.types';
import { problemMessage, toProblem } from '../../core/api/problem';
import { AuthApi } from '../../core/auth/auth-api';
import { AuthStore } from '../../core/auth/auth.store';
import { AuthLayout } from './auth-layout';
import { FormState } from './form-state';

/** AUTH-1: confirm the email address from the verification link (?userId=…&token=…). */
@Component({
  selector: 'ms-verify-email',
  imports: [AuthLayout, RouterLink, MatButtonModule, MatProgressBarModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ms-auth-layout heading="Verify your email">
      @switch (status()) {
        @case ('working') {
          <p role="status">Confirming your email…</p>
          <mat-progress-bar mode="indeterminate" />
        }
        @case ('done') {
          <p role="status">Thanks — your email is confirmed.</p>
          <a mat-flat-button [routerLink]="store.isAuthenticated() ? '/dashboard' : '/login'">Continue</a>
        }
        @case ('failed') {
          <p class="form-error" role="alert">{{ error() }}</p>
          <p>Sign in and use “Resend email” on the banner to get a new link.</p>
          <a mat-stroked-button [routerLink]="store.isAuthenticated() ? '/dashboard' : '/login'">Continue</a>
        }
      }
    </ms-auth-layout>
  `,
})
export class VerifyEmail implements OnInit {
  readonly userId = input<string>();
  readonly token = input<string>();

  protected readonly store = inject(AuthStore);
  private readonly api = inject(AuthApi);
  protected readonly status = signal<'working' | 'done' | 'failed'>('working');
  protected readonly error = signal('');

  async ngOnInit(): Promise<void> {
    if (!this.userId() || !this.token()) {
      this.error.set('This verification link is incomplete.');
      this.status.set('failed');
      return;
    }
    try {
      await firstValueFrom(this.api.verifyEmail(this.userId()!, this.token()!));
      if (this.store.isAuthenticated()) {
        await this.store.reloadMe();
      }
      this.status.set('done');
    } catch (e) {
      this.error.set(problemMessage(toProblem(e)));
      this.status.set('failed');
    }
  }
}

/** AUTH-5: accept an invitation (?token=…) — choose a name and password, then land in the workspace. */
@Component({
  selector: 'ms-accept-invite',
  imports: [AuthLayout, ReactiveFormsModule, RouterLink, MatButtonModule, MatFormFieldModule, MatInputModule, MatProgressBarModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ms-auth-layout heading="Join your team" [subheading]="subheading()">
      @if (loading()) {
        <mat-progress-bar mode="indeterminate" />
      } @else if (invite(); as details) {
        <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
          <p>
            You've been invited to <strong>{{ details.workspaceName }}</strong> as
            <strong>{{ roleLabel(details.role) }}</strong>. You'll sign in with <strong>{{ details.email }}</strong>.
          </p>
          @if (state.error()) {
            <p class="form-error" role="alert">{{ state.error() }}</p>
          }
          @for (message of fieldMessages(); track message) {
            <p class="form-error" role="alert">{{ message }}</p>
          }
          <mat-form-field appearance="outline">
            <mat-label>Your name</mat-label>
            <input matInput formControlName="name" autocomplete="name" required data-testid="name" />
            @if (form.controls.name.invalid) {
              <mat-error>Enter your name</mat-error>
            }
          </mat-form-field>
          <mat-form-field appearance="outline">
            <mat-label>Choose a password</mat-label>
            <input matInput type="password" formControlName="password" autocomplete="new-password" required data-testid="password" />
            <mat-hint>At least 10 characters</mat-hint>
            @if (form.controls.password.invalid) {
              <mat-error>Use at least 10 characters</mat-error>
            }
          </mat-form-field>
          <button mat-flat-button type="submit" [disabled]="state.busy()" data-testid="submit">Accept and continue</button>
          @if (state.busy()) {
            <mat-progress-bar mode="indeterminate" />
          }
        </form>
      } @else {
        <p class="form-error" role="alert">{{ loadError() }}</p>
        <a mat-stroked-button routerLink="/login">Go to sign in</a>
      }
    </ms-auth-layout>
  `,
})
export class AcceptInvite implements OnInit {
  readonly token = input<string>();

  private readonly api = inject(AuthApi);
  private readonly store = inject(AuthStore);
  private readonly router = inject(Router);
  protected readonly state = new FormState();
  protected readonly loading = signal(true);
  protected readonly invite = signal<InviteDetails | null>(null);
  protected readonly loadError = signal('');
  protected readonly subheading = computed(() => this.invite()?.workspaceName);
  protected readonly form = inject(NonNullableFormBuilder).group({
    name: ['', [Validators.required, Validators.maxLength(100)]],
    password: ['', [Validators.required, Validators.minLength(10), Validators.maxLength(128)]],
  });

  async ngOnInit(): Promise<void> {
    try {
      if (!this.token()) {
        throw new Error('missing');
      }
      this.invite.set(await firstValueFrom(this.api.lookupInvite(this.token()!)));
    } catch (e) {
      const problem = toProblem(e);
      this.loadError.set(problem.status > 0 ? problemMessage(problem) : 'This invitation link is incomplete.');
    } finally {
      this.loading.set(false);
    }
  }

  protected roleLabel(role: keyof typeof ROLE_LABELS): string {
    return ROLE_LABELS[role];
  }

  protected fieldMessages(): string[] {
    return Object.values(this.state.fields());
  }

  protected async submit(): Promise<void> {
    const { name, password } = this.form.getRawValue();
    if (await this.state.submit(this.form, () => this.store.acceptInvite(this.token()!, name, password))) {
      await this.router.navigateByUrl('/dashboard');
    }
  }
}
