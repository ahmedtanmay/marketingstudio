import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { firstValueFrom } from 'rxjs';
import { ROLE_LABELS, Role, TENANT_ROLES } from '../../../core/api/api.types';
import { FormState } from '../../auth/form-state';
import { TeamApi } from './team-api';

const ROLE_HELP: Record<Role, string> = {
  TenantAdmin: 'Full access, including users, channels and billing.',
  MarketingManager: 'Manages brands, campaigns and agents; approves content.',
  ContentManager: 'Creates content and brand material; cannot approve or publish.',
  Viewer: 'Read-only access.',
  SuperAdmin: '',
};

/** AUTH-5: invite by email with a role. Plan-limit errors (402) are shown inline with the reason. */
@Component({
  selector: 'ms-invite-dialog',
  imports: [ReactiveFormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatProgressBarModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2 mat-dialog-title>Invite a team member</h2>
    <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
      <mat-dialog-content class="dialog-body">
        @if (state.error()) {
          <p class="form-error" role="alert" data-testid="invite-error">{{ state.error() }}</p>
        }
        @for (message of fieldMessages(); track message) {
          <p class="form-error" role="alert">{{ message }}</p>
        }
        <mat-form-field appearance="outline">
          <mat-label>Email</mat-label>
          <input matInput type="email" formControlName="email" required cdkFocusInitial data-testid="invite-email" />
          @if (form.controls.email.invalid) {
            <mat-error>Enter a valid email</mat-error>
          }
        </mat-form-field>
        <mat-form-field appearance="outline">
          <mat-label>Role</mat-label>
          <mat-select formControlName="role" data-testid="invite-role">
            @for (role of roles; track role) {
              <mat-option [value]="role">{{ labels[role] }}</mat-option>
            }
          </mat-select>
          <mat-hint>{{ help[form.controls.role.value] }}</mat-hint>
        </mat-form-field>
        @if (state.busy()) {
          <mat-progress-bar mode="indeterminate" />
        }
      </mat-dialog-content>
      <mat-dialog-actions align="end">
        <button mat-button type="button" mat-dialog-close>Cancel</button>
        <button mat-flat-button type="submit" [disabled]="state.busy()" data-testid="invite-send">Send invite</button>
      </mat-dialog-actions>
    </form>
  `,
  styles: `
    .dialog-body { display: grid; gap: 8px; min-width: min(420px, 80vw); }
  `,
})
export class InviteDialog {
  private readonly api = inject(TeamApi);
  private readonly dialogRef = inject(MatDialogRef<InviteDialog, boolean>);
  protected readonly state = new FormState();
  protected readonly roles = TENANT_ROLES;
  protected readonly labels = ROLE_LABELS;
  protected readonly help = ROLE_HELP;
  protected readonly form = inject(NonNullableFormBuilder).group({
    email: ['', [Validators.required, Validators.email]],
    role: ['ContentManager' as Role, Validators.required],
  });

  protected fieldMessages(): string[] {
    return Object.values(this.state.fields());
  }

  protected async submit(): Promise<void> {
    const { email, role } = this.form.getRawValue();
    if (await this.state.submit(this.form, () => firstValueFrom(this.api.invite(email, role)))) {
      this.dialogRef.close(true);
    }
  }
}
