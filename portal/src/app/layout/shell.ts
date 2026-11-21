import { BreakpointObserver, Breakpoints } from '@angular/cdk/layout';
import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatDividerModule } from '@angular/material/divider';
import { MatIconModule } from '@angular/material/icon';
import { MatListModule } from '@angular/material/list';
import { MatMenuModule } from '@angular/material/menu';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatToolbarModule } from '@angular/material/toolbar';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { firstValueFrom, map } from 'rxjs';
import { Permission, ROLE_LABELS } from '../core/api/api.types';
import { problemMessage, toProblem } from '../core/api/problem';
import { AuthApi } from '../core/auth/auth-api';
import { AuthStore } from '../core/auth/auth.store';
import { BrandStore } from '../core/brand/brand.store';

interface NavItem {
  label: string;
  icon: string;
  link: string;
  permission?: string;
}

/** Signed-in layout: toolbar, navigation and global banners (UI Screen Flows and Wireframes.md → Navigation). */
@Component({
  selector: 'ms-shell',
  imports: [
    RouterOutlet, RouterLink, RouterLinkActive, MatSidenavModule, MatToolbarModule, MatListModule, MatIconModule,
    MatButtonModule, MatMenuModule, MatDividerModule, MatSelectModule, MatProgressBarModule,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <a class="skip-link" href="#main">Skip to content</a>
    <mat-toolbar class="toolbar">
      @if (isHandset()) {
        <button mat-icon-button (click)="navOpen.set(!navOpen())" aria-label="Toggle navigation">
          <mat-icon>menu</mat-icon>
        </button>
      }
      <span class="product">AI Marketing Studio</span>
      <span class="workspace" data-testid="workspace-name">{{ store.tenant()?.name }}</span>
      @if (brands.hasBrands()) {
        <mat-select class="brand-switcher" [value]="brands.activeBrandId()" (selectionChange)="brands.setActive($event.value)"
                    aria-label="Active brand" data-testid="brand-switcher">
          @for (brand of brands.brands(); track brand.id) {
            <mat-option [value]="brand.id">{{ brand.name }}</mat-option>
          }
        </mat-select>
      }
      <span class="spacer"></span>
      <button mat-button [matMenuTriggerFor]="userMenu" data-testid="user-menu">
        <mat-icon>account_circle</mat-icon>
        {{ store.user()?.displayName }}
      </button>
      <mat-menu #userMenu="matMenu">
        <div class="menu-identity" role="presentation">
          <div>{{ store.user()?.email }}</div>
          <div class="muted">{{ roleLabel() }}</div>
        </div>
        <mat-divider />
        <button mat-menu-item (click)="signOut()" data-testid="sign-out">
          <mat-icon>logout</mat-icon>
          Sign out
        </button>
      </mat-menu>
    </mat-toolbar>

    <mat-sidenav-container class="container">
      <mat-sidenav
        [mode]="isHandset() ? 'over' : 'side'"
        [opened]="!isHandset() || navOpen()"
        (closed)="navOpen.set(false)"
        class="sidenav"
      >
        <nav aria-label="Main">
          <mat-nav-list>
            @for (item of navItems(); track item.link) {
              <a mat-list-item [routerLink]="item.link" routerLinkActive #active="routerLinkActive" [activated]="active.isActive"
                 (click)="navOpen.set(false)">
                <mat-icon matListItemIcon>{{ item.icon }}</mat-icon>
                <span matListItemTitle>{{ item.label }}</span>
              </a>
            }
          </mat-nav-list>
        </nav>
      </mat-sidenav>

      <mat-sidenav-content>
        @if (store.user() && !store.user()!.emailVerified) {
          <div class="banner" role="status" data-testid="verify-banner">
            <mat-icon>mark_email_unread</mat-icon>
            <span>Please confirm your email address — you'll need it to connect channels and publish.</span>
            <button mat-button (click)="resendVerification()" [disabled]="resending()">Resend email</button>
          </div>
        }
        @if (store.subscription()?.status === 'Trialing') {
          <div class="banner trial" role="status" data-testid="trial-banner">
            <mat-icon>schedule</mat-icon>
            <span>Free trial: {{ store.subscription()!.trialDaysRemaining }} days left.</span>
          </div>
        }
        <main id="main" class="content" tabindex="-1">
          @if (ready()) {
            <router-outlet />
          } @else {
            <mat-progress-bar mode="indeterminate" aria-label="Loading workspace" />
          }
        </main>
      </mat-sidenav-content>
    </mat-sidenav-container>
  `,
  styles: `
    :host { display: flex; flex-direction: column; height: 100dvh; }
    .toolbar { gap: 12px; border-bottom: 1px solid var(--mat-sys-outline-variant); }
    .product { font: var(--mat-sys-title-medium); color: var(--mat-sys-primary); }
    .workspace { font: var(--mat-sys-body-medium); color: var(--mat-sys-on-surface-variant); }
    .spacer { flex: 1; }
    .brand-switcher { width: 200px; font: var(--mat-sys-body-medium); }
    .container { flex: 1; }
    .sidenav { width: 240px; border-right: 1px solid var(--mat-sys-outline-variant); }
    .content { padding: 24px; max-width: 1200px; outline: none; }
    .banner {
      display: flex; align-items: center; gap: 12px; padding: 8px 24px;
      background: var(--mat-sys-secondary-container); color: var(--mat-sys-on-secondary-container);
    }
    .banner.trial { background: var(--mat-sys-surface-container-high); color: var(--mat-sys-on-surface); }
    .menu-identity { padding: 8px 16px; font: var(--mat-sys-body-medium); }
    .muted { color: var(--mat-sys-on-surface-variant); }
    @media (max-width: 599px) {
      .workspace { display: none; }
      .content { padding: 16px; }
    }
  `,
})
export class Shell implements OnInit {
  protected readonly store = inject(AuthStore);
  protected readonly brands = inject(BrandStore);
  protected readonly ready = signal(false);
  private readonly api = inject(AuthApi);
  private readonly router = inject(Router);
  private readonly snackBar = inject(MatSnackBar);

  protected readonly navOpen = signal(false);
  protected readonly resending = signal(false);
  protected readonly isHandset = toSignal(
    inject(BreakpointObserver).observe(Breakpoints.Handset).pipe(map((r) => r.matches)),
    { initialValue: false },
  );

  private readonly allNavItems: NavItem[] = [
    { label: 'Dashboard', icon: 'dashboard', link: '/dashboard' },
    { label: 'Brand profile', icon: 'storefront', link: '/brand/profile', permission: Permission.BrandsRead },
    { label: 'Products', icon: 'inventory_2', link: '/brand/products', permission: Permission.ProductsRead },
    { label: 'Media', icon: 'photo_library', link: '/brand/media', permission: Permission.BrandsRead },
    { label: 'All brands', icon: 'apps', link: '/brands', permission: Permission.BrandsRead },
    { label: 'Team', icon: 'group', link: '/settings/users', permission: Permission.UsersManage },
  ];

  protected readonly navItems = computed(() =>
    this.allNavItems.filter((item) => !item.permission || this.store.permissions().has(item.permission)),
  );

  protected readonly roleLabel = computed(() => {
    const role = this.store.user()?.roles[0];
    return role ? ROLE_LABELS[role] : '';
  });

  protected async resendVerification(): Promise<void> {
    this.resending.set(true);
    try {
      await firstValueFrom(this.api.resendVerification());
      this.snackBar.open('Verification email sent. Check your inbox.', 'OK', { duration: 5000 });
    } catch (e) {
      this.snackBar.open(problemMessage(toProblem(e)), 'OK', { duration: 6000 });
    } finally {
      this.resending.set(false);
    }
  }

  async ngOnInit(): Promise<void> {
    try {
      if (this.store.can(Permission.BrandsRead)) {
        await this.brands.load();
      }
    } catch (e) {
      this.snackBar.open(problemMessage(toProblem(e)), 'OK', { duration: 6000 });
    } finally {
      this.ready.set(true);
    }
  }

  protected async signOut(): Promise<void> {
    this.brands.reset();
    await this.store.logout();
    await this.router.navigateByUrl('/login');
  }
}
