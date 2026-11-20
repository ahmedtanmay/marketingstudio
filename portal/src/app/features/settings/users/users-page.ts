import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatChipsModule } from '@angular/material/chips';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTableModule } from '@angular/material/table';
import { MatTabsModule } from '@angular/material/tabs';
import { Observable, firstValueFrom } from 'rxjs';
import { InviteListItem, ROLE_LABELS, Role, TENANT_ROLES, UserListItem } from '../../../core/api/api.types';
import { problemMessage, toProblem } from '../../../core/api/problem';
import { AuthStore } from '../../../core/auth/auth.store';
import { InviteDialog } from './invite-dialog';
import { TeamApi } from './team-api';

/** AUTH-5 / AUTH-6: members, roles and pending invites (Tenant Admin). */
@Component({
  selector: 'ms-users-page',
  imports: [
    DatePipe, MatTableModule, MatTabsModule, MatButtonModule, MatIconModule, MatMenuModule, MatSelectModule, MatChipsModule,
    MatProgressBarModule,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="header">
      <div>
        <h1 class="page-title">Team</h1>
        <p class="muted">Invite people and choose what they can do. Pending invites count toward your plan's seats.</p>
      </div>
      <button mat-flat-button (click)="openInvite()" data-testid="invite-button">
        <mat-icon>person_add</mat-icon>
        Invite
      </button>
    </div>

    @if (loading()) {
      <mat-progress-bar mode="indeterminate" />
    }
    @if (loadError()) {
      <p class="form-error" role="alert">{{ loadError() }}</p>
    }

    <mat-tab-group>
      <mat-tab [label]="'Members (' + users().length + ')'">
        <table mat-table [dataSource]="users()" class="table" data-testid="members-table">
          <ng-container matColumnDef="name">
            <th mat-header-cell *matHeaderCellDef>Name</th>
            <td mat-cell *matCellDef="let user">
              <div>{{ user.displayName }}@if (user.id === myId()) { <span class="muted"> (you)</span> }</div>
              <div class="muted small">{{ user.email }}</div>
            </td>
          </ng-container>
          <ng-container matColumnDef="role">
            <th mat-header-cell *matHeaderCellDef>Role</th>
            <td mat-cell *matCellDef="let user">
              <mat-select [value]="user.role" (selectionChange)="changeRole(user, $event.value)"
                          [disabled]="user.status !== 'Active'" [aria-label]="'Role for ' + user.displayName"
                          class="role-select">
                @for (role of roles; track role) {
                  <mat-option [value]="role">{{ labels[role] }}</mat-option>
                }
              </mat-select>
            </td>
          </ng-container>
          <ng-container matColumnDef="status">
            <th mat-header-cell *matHeaderCellDef>Status</th>
            <td mat-cell *matCellDef="let user">
              <mat-chip-set><mat-chip [highlighted]="user.status === 'Active'">{{ user.status }}</mat-chip></mat-chip-set>
              @if (!user.emailVerified) { <div class="muted small">Email not confirmed</div> }
            </td>
          </ng-container>
          <ng-container matColumnDef="lastLogin">
            <th mat-header-cell *matHeaderCellDef>Last sign-in</th>
            <td mat-cell *matCellDef="let user">{{ user.lastLoginAt ? (user.lastLoginAt | date: 'medium') : 'Never' }}</td>
          </ng-container>
          <ng-container matColumnDef="actions">
            <th mat-header-cell *matHeaderCellDef><span class="sr-only">Actions</span></th>
            <td mat-cell *matCellDef="let user">
              @if (user.id !== myId()) {
                <button mat-icon-button [matMenuTriggerFor]="menu" [attr.aria-label]="'Actions for ' + user.displayName">
                  <mat-icon>more_vert</mat-icon>
                </button>
                <mat-menu #menu="matMenu">
                  @if (user.status === 'Active') {
                    <button mat-menu-item (click)="deactivate(user)">Deactivate</button>
                  } @else {
                    <button mat-menu-item (click)="reactivate(user)">Reactivate</button>
                  }
                </mat-menu>
              }
            </td>
          </ng-container>
          <tr mat-header-row *matHeaderRowDef="userColumns"></tr>
          <tr mat-row *matRowDef="let row; columns: userColumns"></tr>
        </table>
      </mat-tab>

      <mat-tab [label]="'Pending invites (' + invites().length + ')'">
        @if (invites().length === 0) {
          <p class="empty">No pending invites. Use “Invite” to add people to your workspace.</p>
        } @else {
          <table mat-table [dataSource]="invites()" class="table" data-testid="invites-table">
            <ng-container matColumnDef="email">
              <th mat-header-cell *matHeaderCellDef>Email</th>
              <td mat-cell *matCellDef="let invite">{{ invite.email }}</td>
            </ng-container>
            <ng-container matColumnDef="role">
              <th mat-header-cell *matHeaderCellDef>Role</th>
              <td mat-cell *matCellDef="let invite">{{ roleLabel(invite.role) }}</td>
            </ng-container>
            <ng-container matColumnDef="expires">
              <th mat-header-cell *matHeaderCellDef>Expires</th>
              <td mat-cell *matCellDef="let invite">{{ invite.expiresAt | date: 'mediumDate' }}</td>
            </ng-container>
            <ng-container matColumnDef="actions">
              <th mat-header-cell *matHeaderCellDef><span class="sr-only">Actions</span></th>
              <td mat-cell *matCellDef="let invite">
                <button mat-button (click)="resend(invite)">Resend</button>
                <button mat-button (click)="revoke(invite)">Revoke</button>
              </td>
            </ng-container>
            <tr mat-header-row *matHeaderRowDef="inviteColumns"></tr>
            <tr mat-row *matRowDef="let row; columns: inviteColumns"></tr>
          </table>
        }
      </mat-tab>
    </mat-tab-group>
  `,
  styles: `
    .header { display: flex; justify-content: space-between; align-items: flex-start; gap: 16px; flex-wrap: wrap; }
    .page-title { font: var(--mat-sys-headline-medium); margin: 0 0 4px; }
    .muted { color: var(--mat-sys-on-surface-variant); }
    .small { font: var(--mat-sys-body-small); }
    .table { width: 100%; }
    .role-select { min-width: 180px; }
    .empty { padding: 24px 0; color: var(--mat-sys-on-surface-variant); }
    :host { display: block; overflow-x: auto; }
  `,
})
export class UsersPage implements OnInit {
  private readonly api = inject(TeamApi);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);
  private readonly store = inject(AuthStore);

  protected readonly users = signal<UserListItem[]>([]);
  protected readonly invites = signal<InviteListItem[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadError = signal<string | null>(null);
  protected readonly myId = () => this.store.user()?.id;
  protected readonly roles = TENANT_ROLES;
  protected readonly labels = ROLE_LABELS;
  protected readonly userColumns = ['name', 'role', 'status', 'lastLogin', 'actions'];
  protected readonly inviteColumns = ['email', 'role', 'expires', 'actions'];

  protected roleLabel(role: Role): string {
    return ROLE_LABELS[role] ?? role;
  }

  async ngOnInit(): Promise<void> {
    await this.reload();
  }

  protected async openInvite(): Promise<void> {
    const sent = await firstValueFrom(this.dialog.open(InviteDialog, { autoFocus: 'first-tabbable' }).afterClosed());
    if (sent) {
      this.notify('Invite sent.');
      await this.reload();
    }
  }

  protected changeRole(user: UserListItem, role: Role): Promise<void> {
    return this.run(this.api.changeRole(user.id, role), `${user.displayName} is now ${ROLE_LABELS[role]}.`);
  }

  protected deactivate(user: UserListItem): Promise<void> {
    return this.run(this.api.deactivate(user.id), `${user.displayName} has been deactivated and signed out.`);
  }

  protected reactivate(user: UserListItem): Promise<void> {
    return this.run(this.api.reactivate(user.id), `${user.displayName} has been reactivated.`);
  }

  protected resend(invite: InviteListItem): Promise<void> {
    return this.run(this.api.resendInvite(invite.id), `Invite re-sent to ${invite.email}.`);
  }

  protected revoke(invite: InviteListItem): Promise<void> {
    return this.run(this.api.revokeInvite(invite.id), `Invite to ${invite.email} revoked.`);
  }

  /** Runs an action, reports the result, and reloads — so a refused change snaps back to the saved state. */
  private async run(action: Observable<unknown>, success: string): Promise<void> {
    try {
      await firstValueFrom(action, { defaultValue: undefined });
      this.notify(success);
    } catch (e) {
      this.notify(problemMessage(toProblem(e)));
    }
    await this.reload();
  }

  private async reload(): Promise<void> {
    this.loading.set(true);
    try {
      const [users, invites] = await Promise.all([firstValueFrom(this.api.listUsers()), firstValueFrom(this.api.listInvites())]);
      this.users.set(users);
      this.invites.set(invites);
      this.loadError.set(null);
    } catch (e) {
      this.loadError.set(problemMessage(toProblem(e)));
    } finally {
      this.loading.set(false);
    }
  }

  private notify(message: string): void {
    this.snackBar.open(message, 'OK', { duration: 5000 });
  }
}
