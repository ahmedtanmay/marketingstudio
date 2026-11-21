import { ChangeDetectionStrategy, Component, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTabsModule } from '@angular/material/tabs';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { Brand, BrandProfileInput, BrandVoice, Permission } from '../../core/api/api.types';
import { problemMessage, toProblem } from '../../core/api/problem';
import { AuthStore } from '../../core/auth/auth.store';
import { BrandApi } from '../../core/brand/brand-api';
import { BrandStore } from '../../core/brand/brand.store';
import { BrandProfileForm } from './brand-profile-form';
import { VoiceForm } from './voice-form';

/** Shown on brand pages when the workspace has no brand yet. */
@Component({
  selector: 'ms-no-brand',
  imports: [RouterLink, MatButtonModule, MatIconModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="empty" role="status">
      <mat-icon aria-hidden="true">storefront</mat-icon>
      <h2>No brand yet</h2>
      <p>Create a brand so agents know your name, voice and products.</p>
      @if (auth.can(writePermission)) {
        <a mat-flat-button routerLink="/brands/new" data-testid="create-first-brand">Create your first brand</a>
      } @else {
        <p class="muted">Ask a Marketing Manager or Tenant Admin to create one.</p>
      }
    </div>
  `,
  styles: `
    .empty { text-align: center; padding: 48px 16px; display: grid; justify-items: center; gap: 8px; }
    .empty mat-icon { font-size: 48px; width: 48px; height: 48px; color: var(--mat-sys-primary); }
    h2 { font: var(--mat-sys-title-large); margin: 0; }
    .muted { color: var(--mat-sys-on-surface-variant); }
  `,
})
export class NoBrand {
  protected readonly auth = inject(AuthStore);
  protected readonly writePermission = Permission.BrandsWrite;
}

/** BRD-4: all brands of the workspace; switch or create. */
@Component({
  selector: 'ms-brands-page',
  imports: [RouterLink, MatCardModule, MatButtonModule, MatIconModule, NoBrand],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="header">
      <h1 class="page-title">Brands</h1>
      @if (auth.can(writePermission) && brands.hasBrands()) {
        <a mat-flat-button routerLink="/brands/new" data-testid="new-brand"><mat-icon>add</mat-icon> New brand</a>
      }
    </div>
    @if (!brands.hasBrands()) {
      <ms-no-brand />
    } @else {
      <div class="grid">
        @for (brand of brands.brands(); track brand.id) {
          <mat-card appearance="outlined" [class.active]="brand.id === brands.activeBrandId()">
            <mat-card-header>
              <mat-card-title><h2>{{ brand.name }}</h2></mat-card-title>
              <mat-card-subtitle>{{ brand.industry }}</mat-card-subtitle>
            </mat-card-header>
            <mat-card-actions>
              @if (brand.id === brands.activeBrandId()) {
                <span class="current">Current brand</span>
              } @else {
                <button mat-button (click)="brands.setActive(brand.id)">Switch to this brand</button>
              }
              <a mat-button routerLink="/brand/profile" (click)="brands.setActive(brand.id)">Settings</a>
            </mat-card-actions>
          </mat-card>
        }
      </div>
    }
  `,
  styles: `
    .header { display: flex; justify-content: space-between; align-items: center; gap: 16px; margin-bottom: 16px; }
    .page-title { font: var(--mat-sys-headline-medium); margin: 0; }
    .grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(260px, 1fr)); gap: 16px; }
    h2 { font: var(--mat-sys-title-medium); margin: 0; }
    mat-card.active { border-color: var(--mat-sys-primary); }
    .current { font: var(--mat-sys-label-large); color: var(--mat-sys-primary); padding: 0 12px; }
  `,
})
export class BrandsPage {
  protected readonly brands = inject(BrandStore);
  protected readonly auth = inject(AuthStore);
  protected readonly writePermission = Permission.BrandsWrite;
}

/** BRD-1: create a brand, then make it the active brand. */
@Component({
  selector: 'ms-create-brand',
  imports: [BrandProfileForm],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h1 class="page-title">Create a brand</h1>
    <p class="muted">Agents use this to write in the right language and time zone. You can change it later.</p>
    <ms-brand-profile-form submitLabel="Create brand" [save]="create" />
  `,
  styles: `
    .page-title { font: var(--mat-sys-headline-medium); margin: 0 0 4px; }
    .muted { color: var(--mat-sys-on-surface-variant); margin: 0 0 24px; }
  `,
})
export class CreateBrand {
  private readonly api = inject(BrandApi);
  private readonly brands = inject(BrandStore);
  private readonly router = inject(Router);
  private readonly snackBar = inject(MatSnackBar);

  protected readonly create = async (profile: BrandProfileInput): Promise<void> => {
    const brand = await firstValueFrom(this.api.createBrand(profile));
    await this.brands.refresh(brand.id);
    this.snackBar.open(`${brand.name} created.`, 'OK', { duration: 4000 });
    await this.router.navigateByUrl('/brand/profile');
  };
}

/** BRD-1/2/5: profile, voice and delete for the active brand. Controls follow the user's permissions. */
@Component({
  selector: 'ms-brand-settings',
  imports: [FormsModule, MatTabsModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatProgressBarModule, BrandProfileForm, VoiceForm, NoBrand],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (!brands.activeBrandId()) {
      <ms-no-brand />
    } @else {
      <h1 class="page-title">{{ brand()?.name ?? brands.activeBrand()?.name }}</h1>
      @if (loading()) {
        <mat-progress-bar mode="indeterminate" />
      }
      @if (loadError()) {
        <p class="form-error" role="alert">{{ loadError() }}</p>
      }
      @if (brand(); as current) {
        <mat-tab-group>
          <mat-tab label="Profile">
            <div class="tab">
              @if (!auth.can(permission.BrandsWrite)) {
                <p class="muted">You can view the profile. Marketing Managers and Tenant Admins can change it.</p>
              }
              <ms-brand-profile-form [initial]="current" [readOnly]="!auth.can(permission.BrandsWrite)" [save]="saveProfile"
                                     (saved)="notify('Profile saved.')" />
            </div>
          </mat-tab>
          <mat-tab label="Voice">
            <div class="tab">
              <ms-voice-form [initial]="current.voice" [readOnly]="!auth.can(permission.BrandContentWrite)" [save]="saveVoice"
                             (saved)="notify('Voice saved.')" />
            </div>
          </mat-tab>
          @if (auth.can(permission.BrandsDelete)) {
            <mat-tab label="Delete">
              <div class="tab danger">
                <h2>Delete this brand</h2>
                <p>This permanently deletes <strong>{{ current.name }}</strong>, its products and its media library. It can't be undone.</p>
                <form (ngSubmit)="deleteBrand(current)" class="delete-form">
                  <mat-form-field appearance="outline">
                    <mat-label>Brand name</mat-label>
                    <input matInput name="confirmName" [(ngModel)]="confirmName" data-testid="delete-confirm" autocomplete="off" />
                    <mat-hint>Type “{{ current.name }}” exactly to confirm</mat-hint>
                  </mat-form-field>
                  @if (deleteError()) {
                    <p class="form-error" role="alert">{{ deleteError() }}</p>
                  }
                  <div>
                    <button mat-flat-button type="submit" class="danger-button" [disabled]="confirmName !== current.name || deleting()"
                            data-testid="delete-brand">Delete brand</button>
                  </div>
                </form>
              </div>
            </mat-tab>
          }
        </mat-tab-group>
      }
    }
  `,
  styles: `
    .page-title { font: var(--mat-sys-headline-medium); margin: 0 0 16px; }
    .tab { padding: 24px 0; }
    .muted { color: var(--mat-sys-on-surface-variant); }
    .danger h2 { font: var(--mat-sys-title-large); margin: 0 0 8px; color: var(--mat-sys-error); }
    .delete-form { max-width: 420px; }
    .danger-button:not([disabled]) { background: var(--mat-sys-error); color: var(--mat-sys-on-error); }
  `,
})
export class BrandSettings {
  protected readonly brands = inject(BrandStore);
  protected readonly auth = inject(AuthStore);
  private readonly api = inject(BrandApi);
  private readonly router = inject(Router);
  private readonly snackBar = inject(MatSnackBar);

  protected readonly permission = Permission;
  protected readonly brand = signal<Brand | null>(null);
  protected readonly loading = signal(false);
  protected readonly loadError = signal<string | null>(null);
  protected readonly deleting = signal(false);
  protected readonly deleteError = signal<string | null>(null);
  protected confirmName = '';

  constructor() {
    // Reload whenever the active brand changes (header switcher).
    effect(() => {
      const id = this.brands.activeBrandId();
      this.brand.set(null);
      this.confirmName = '';
      if (id) {
        void this.load(id);
      }
    });
  }

  protected readonly saveProfile = async (profile: BrandProfileInput): Promise<void> => {
    const id = this.brands.activeBrandId()!;
    this.brand.set(await firstValueFrom(this.api.updateProfile(id, profile)));
    await this.brands.refresh();
  };

  protected readonly saveVoice = async (voice: BrandVoice): Promise<void> => {
    this.brand.set(await firstValueFrom(this.api.updateVoice(this.brands.activeBrandId()!, voice)));
  };

  protected notify(message: string): void {
    this.snackBar.open(message, 'OK', { duration: 4000 });
  }

  protected async deleteBrand(brand: Brand): Promise<void> {
    this.deleting.set(true);
    this.deleteError.set(null);
    try {
      await firstValueFrom(this.api.deleteBrand(brand.id, this.confirmName));
      await this.brands.refresh();
      this.notify(`${brand.name} was deleted.`);
      await this.router.navigateByUrl('/brands');
    } catch (e) {
      this.deleteError.set(problemMessage(toProblem(e)));
    } finally {
      this.deleting.set(false);
    }
  }

  private async load(id: string): Promise<void> {
    this.loading.set(true);
    this.loadError.set(null);
    try {
      const brand = await firstValueFrom(this.api.getBrand(id));
      if (this.brands.activeBrandId() === id) {
        this.brand.set(brand);
      }
    } catch (e) {
      this.loadError.set(problemMessage(toProblem(e)));
    } finally {
      this.loading.set(false);
    }
  }
}
