import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { RouterLink } from '@angular/router';
import { Permission } from '../../core/api/api.types';
import { AuthStore } from '../../core/auth/auth.store';

interface ChecklistItem {
  label: string;
  done: boolean;
  link?: string;
  available: boolean;
}

/**
 * Dashboard placeholder for Sprint 2. Widgets (ANL-1) arrive in Sprint 8; the onboarding checklist
 * (TEN-1) is completed in Sprint 3 when brands and products exist.
 */
@Component({
  selector: 'ms-dashboard',
  imports: [MatCardModule, MatIconModule, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h1 class="page-title">Welcome, {{ firstName() }}</h1>
    <p class="muted">Here's how to get {{ store.tenant()?.name }} ready.</p>

    <mat-card appearance="outlined" class="checklist">
      <mat-card-header>
        <mat-card-title><h2>Getting started</h2></mat-card-title>
      </mat-card-header>
      <mat-card-content>
        <ul>
          @for (item of checklist(); track item.label) {
            <li [class.done]="item.done">
              <mat-icon aria-hidden="true">{{ item.done ? 'check_circle' : 'radio_button_unchecked' }}</mat-icon>
              @if (item.link && item.available) {
                <a [routerLink]="item.link">{{ item.label }}</a>
              } @else {
                <span>{{ item.label }}</span>
              }
              @if (!item.available) {
                <span class="soon">Coming soon</span>
              }
              <span class="sr-only">{{ item.done ? '(done)' : '(to do)' }}</span>
            </li>
          }
        </ul>
      </mat-card-content>
    </mat-card>
  `,
  styles: `
    .page-title { font: var(--mat-sys-headline-medium); margin: 0 0 4px; }
    .muted { color: var(--mat-sys-on-surface-variant); margin: 0 0 24px; }
    h2 { font: var(--mat-sys-title-large); margin: 0; }
    .checklist { max-width: 640px; }
    ul { list-style: none; padding: 0; margin: 8px 0 0; display: grid; gap: 12px; }
    li { display: flex; align-items: center; gap: 12px; }
    li.done mat-icon { color: var(--mat-sys-primary); }
    .soon { font: var(--mat-sys-label-small); color: var(--mat-sys-on-surface-variant); }
  `,
})
export class Dashboard {
  protected readonly store = inject(AuthStore);

  protected readonly firstName = computed(() => this.store.user()?.displayName.split(' ')[0] ?? '');

  protected readonly checklist = computed<ChecklistItem[]>(() => [
    { label: 'Create your workspace', done: true, available: true },
    { label: 'Confirm your email', done: this.store.user()?.emailVerified ?? false, available: true },
    {
      label: 'Invite your team',
      done: false,
      link: '/settings/users',
      available: this.store.can(Permission.UsersManage),
    },
    { label: 'Create your first brand', done: false, available: false },
    { label: 'Upload brand knowledge', done: false, available: false },
    { label: 'Connect a social channel', done: false, available: false },
  ]);
}
