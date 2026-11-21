import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { OnboardingStatus, Permission } from '../../core/api/api.types';
import { AuthStore } from '../../core/auth/auth.store';
import { BrandApi } from '../../core/brand/brand-api';

interface ChecklistItem {
  label: string;
  done: boolean;
  link?: string;
  /** Feature not built yet (later sprint). */
  comingSoon?: boolean;
  testId: string;
}

/**
 * TEN-1: getting-started checklist. Steps tick themselves from real data (GET /api/v1/onboarding).
 * Dashboard widgets (ANL-1) arrive in Sprint 8.
 */
@Component({
  selector: 'ms-dashboard',
  imports: [MatCardModule, MatIconModule, MatProgressBarModule, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h1 class="page-title">Welcome, {{ firstName() }}</h1>
    <p class="muted">Here's how to get {{ auth.tenant()?.name }} ready.</p>

    <mat-card appearance="outlined" class="checklist">
      <mat-card-header>
        <mat-card-title><h2>Getting started</h2></mat-card-title>
        <mat-card-subtitle>{{ doneCount() }} of {{ items().length }} done</mat-card-subtitle>
      </mat-card-header>
      <mat-card-content>
        @if (!status()) {
          <mat-progress-bar mode="indeterminate" />
        }
        <ul>
          @for (item of items(); track item.label) {
            <li [class.done]="item.done" [attr.data-testid]="item.testId" [attr.data-done]="item.done">
              <mat-icon aria-hidden="true">{{ item.done ? 'check_circle' : 'radio_button_unchecked' }}</mat-icon>
              @if (item.link && !item.done && !item.comingSoon) {
                <a [routerLink]="item.link">{{ item.label }}</a>
              } @else {
                <span>{{ item.label }}</span>
              }
              @if (item.comingSoon) {
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
    li.done span { color: var(--mat-sys-on-surface-variant); }
    .soon { font: var(--mat-sys-label-small); color: var(--mat-sys-on-surface-variant); }
  `,
})
export class Dashboard implements OnInit {
  protected readonly auth = inject(AuthStore);
  private readonly api = inject(BrandApi);
  protected readonly status = signal<OnboardingStatus | null>(null);

  protected readonly firstName = computed(() => this.auth.user()?.displayName.split(' ')[0] ?? '');

  protected readonly items = computed<ChecklistItem[]>(() => {
    const s = this.status();
    const can = (p: string) => this.auth.can(p);
    return [
      { label: 'Create your workspace', done: true, testId: 'step-workspace' },
      { label: 'Confirm your email', done: s?.emailVerified ?? this.auth.user()?.emailVerified ?? false, testId: 'step-email' },
      { label: 'Create your first brand', done: s?.brandCreated ?? false, link: can(Permission.BrandsWrite) ? '/brands/new' : undefined, testId: 'step-brand' },
      { label: 'Add your products', done: s?.productAdded ?? false, link: '/brand/products', testId: 'step-products' },
      { label: 'Invite your team', done: s?.teamInvited ?? false, link: can(Permission.UsersManage) ? '/settings/users' : undefined, testId: 'step-team' },
      { label: 'Upload brand knowledge', done: s?.knowledgeUploaded ?? false, link: '/brand/knowledge', testId: 'step-knowledge' },
      { label: 'Connect a social channel', done: s?.channelConnected ?? false, comingSoon: true, testId: 'step-channel' },
      { label: 'Generate your first post', done: s?.firstPostGenerated ?? false, comingSoon: true, testId: 'step-post' },
    ];
  });

  protected readonly doneCount = computed(() => this.items().filter((i) => i.done).length);

  async ngOnInit(): Promise<void> {
    try {
      this.status.set(await firstValueFrom(this.api.onboarding()));
    } catch {
      // The checklist is a convenience; the dashboard still works without it.
    }
  }
}
