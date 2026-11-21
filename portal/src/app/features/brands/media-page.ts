import { DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatChipsModule } from '@angular/material/chips';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSnackBar } from '@angular/material/snack-bar';
import { firstValueFrom } from 'rxjs';
import { MediaAsset, Permission } from '../../core/api/api.types';
import { problemMessage, toProblem } from '../../core/api/problem';
import { AuthStore } from '../../core/auth/auth.store';
import { BrandApi } from '../../core/brand/brand-api';
import { BrandStore } from '../../core/brand/brand.store';
import { NoBrand } from './brand-pages';

const ACCEPTED = ['image/jpeg', 'image/png', 'image/webp'];
const MAX_BYTES = 10 * 1024 * 1024;

const parseTags = (text: string): string[] =>
  [...new Set(text.split(',').map((t) => t.trim().toLowerCase()).filter((t) => t.length > 0))];

/** BRD-3: brand media library — upload (JPEG/PNG/WebP, ≤ 10 MB), tag, filter, delete. */
@Component({
  selector: 'ms-media-page',
  imports: [DecimalPipe, FormsModule, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule, MatChipsModule, MatProgressBarModule, NoBrand],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (!brands.activeBrandId()) {
      <ms-no-brand />
    } @else {
      <h1 class="page-title">Media library</h1>

      @if (canWrite()) {
        <div class="dropzone" [class.dragging]="dragging()" (dragover)="onDragOver($event)" (dragleave)="dragging.set(false)"
             (drop)="onDrop($event)">
          <mat-icon aria-hidden="true">cloud_upload</mat-icon>
          <p>Drag images here or</p>
          <label class="upload-button">
            <span mat-stroked-button>Choose files</span>
            <input type="file" multiple accept="image/jpeg,image/png,image/webp" (change)="onPick($event)" data-testid="media-input" />
          </label>
          <p class="muted">JPEG, PNG or WebP, up to 10 MB each</p>
          <mat-form-field appearance="outline" class="upload-tags">
            <mat-label>Tags for new uploads (optional)</mat-label>
            <input matInput [(ngModel)]="uploadTags" placeholder="logo, hero" />
          </mat-form-field>
        </div>
      }
      @if (uploading() > 0) {
        <p role="status">Uploading {{ uploading() }} file(s)…</p>
        <mat-progress-bar mode="indeterminate" />
      }
      @for (message of messages(); track message) {
        <p class="form-error" role="alert">{{ message }}</p>
      }

      @if (allTags().length > 0) {
        <mat-chip-listbox aria-label="Filter by tag" [value]="tagFilter()" (change)="setFilter($event.value)" class="filter">
          <mat-chip-option [value]="null">All</mat-chip-option>
          @for (tag of allTags(); track tag) {
            <mat-chip-option [value]="tag">{{ tag }}</mat-chip-option>
          }
        </mat-chip-listbox>
      }

      @if (loading()) {
        <mat-progress-bar mode="indeterminate" />
      }
      @if (!loading() && visible().length === 0) {
        <p class="empty" role="status">No images yet.</p>
      }
      <ul class="grid" data-testid="media-grid">
        @for (asset of visible(); track asset.id) {
          <li class="tile">
            <img [src]="asset.url" [alt]="asset.fileName" loading="lazy" />
            <div class="meta">
              <div class="name" [title]="asset.fileName">{{ asset.fileName }}</div>
              <div class="muted small">{{ asset.sizeBytes / 1024 | number: '1.0-0' }} KB</div>
              @if (editing() === asset.id) {
                <input class="tag-input" [ngModel]="asset.tags.join(', ')" #tagBox
                       (keydown.enter)="saveTags(asset, tagBox.value)" (keydown.escape)="editing.set(null)"
                       [attr.aria-label]="'Tags for ' + asset.fileName" />
                <button mat-button (click)="saveTags(asset, tagBox.value)">Save</button>
              } @else {
                <div class="tags">
                  @for (tag of asset.tags; track tag) { <span class="tag">{{ tag }}</span> }
                </div>
              }
            </div>
            @if (canWrite()) {
              <div class="actions">
                <button mat-icon-button (click)="editing.set(asset.id)" [attr.aria-label]="'Edit tags for ' + asset.fileName">
                  <mat-icon>sell</mat-icon>
                </button>
                <button mat-icon-button (click)="remove(asset)" [attr.aria-label]="'Delete ' + asset.fileName">
                  <mat-icon>delete</mat-icon>
                </button>
              </div>
            }
          </li>
        }
      </ul>
    }
  `,
  styles: `
    .page-title { font: var(--mat-sys-headline-medium); margin: 0 0 16px; }
    .dropzone { border: 2px dashed var(--mat-sys-outline-variant); border-radius: 12px; padding: 16px; text-align: center; margin-bottom: 16px; }
    .dropzone.dragging { border-color: var(--mat-sys-primary); background: var(--mat-sys-surface-container); }
    .dropzone mat-icon { font-size: 36px; width: 36px; height: 36px; color: var(--mat-sys-primary); }
    .dropzone p { margin: 4px 0; }
    .upload-button input { position: absolute; width: 1px; height: 1px; opacity: 0; }
    .upload-button:focus-within span { outline: 2px solid var(--mat-sys-primary); }
    .upload-button span { cursor: pointer; display: inline-block; padding: 8px 16px; border: 1px solid var(--mat-sys-outline); border-radius: 20px; }
    .upload-tags { max-width: 320px; margin-top: 8px; }
    .filter { margin-bottom: 12px; display: block; }
    .grid { list-style: none; padding: 0; margin: 0; display: grid; grid-template-columns: repeat(auto-fill, minmax(180px, 1fr)); gap: 12px; }
    .tile { border: 1px solid var(--mat-sys-outline-variant); border-radius: 12px; overflow: hidden; display: flex; flex-direction: column; }
    .tile img { width: 100%; aspect-ratio: 1; object-fit: cover; background: var(--mat-sys-surface-container); }
    .meta { padding: 8px; display: grid; gap: 4px; }
    .name { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .tags { display: flex; flex-wrap: wrap; gap: 4px; }
    .tag { font: var(--mat-sys-label-small); padding: 2px 8px; border-radius: 8px; background: var(--mat-sys-secondary-container); }
    .tag-input { width: 100%; box-sizing: border-box; }
    .actions { display: flex; justify-content: flex-end; }
    .muted { color: var(--mat-sys-on-surface-variant); }
    .small { font: var(--mat-sys-body-small); }
    .empty { padding: 24px 0; color: var(--mat-sys-on-surface-variant); }
  `,
})
export class MediaPage {
  protected readonly brands = inject(BrandStore);
  private readonly auth = inject(AuthStore);
  private readonly api = inject(BrandApi);
  private readonly snackBar = inject(MatSnackBar);

  protected readonly assets = signal<MediaAsset[]>([]);
  protected readonly loading = signal(false);
  protected readonly uploading = signal(0);
  protected readonly dragging = signal(false);
  protected readonly messages = signal<string[]>([]);
  protected readonly editing = signal<string | null>(null);
  protected readonly tagFilter = signal<string | null>(null);
  protected uploadTags = '';
  protected readonly canWrite = () => this.auth.can(Permission.BrandContentWrite);
  protected readonly allTags = computed(() => [...new Set(this.assets().flatMap((a) => a.tags))].sort());
  protected readonly visible = computed(() => {
    const tag = this.tagFilter();
    return tag ? this.assets().filter((a) => a.tags.includes(tag)) : this.assets();
  });

  constructor() {
    effect(() => {
      if (this.brands.activeBrandId()) {
        this.tagFilter.set(null);
        void this.reload();
      }
    });
  }

  protected setFilter(tag: string | null): void {
    this.tagFilter.set(tag ?? null);
  }

  protected onDragOver(event: DragEvent): void {
    event.preventDefault();
    this.dragging.set(true);
  }

  protected onDrop(event: DragEvent): void {
    event.preventDefault();
    this.dragging.set(false);
    void this.upload([...(event.dataTransfer?.files ?? [])]);
  }

  protected onPick(event: Event): void {
    const input = event.target as HTMLInputElement;
    void this.upload([...(input.files ?? [])]);
    input.value = '';
  }

  protected async saveTags(asset: MediaAsset, text: string): Promise<void> {
    try {
      const updated = await firstValueFrom(this.api.updateMediaTags(asset.brandId, asset.id, parseTags(text)));
      this.assets.update((list) => list.map((a) => (a.id === updated.id ? updated : a)));
      this.editing.set(null);
    } catch (e) {
      this.snackBar.open(problemMessage(toProblem(e)), 'OK', { duration: 6000 });
    }
  }

  protected async remove(asset: MediaAsset): Promise<void> {
    if (!confirm(`Delete ${asset.fileName}? It will also be removed from any products using it.`)) {
      return;
    }
    try {
      await firstValueFrom(this.api.deleteMedia(asset.brandId, asset.id), { defaultValue: undefined });
      this.assets.update((list) => list.filter((a) => a.id !== asset.id));
      this.snackBar.open(`${asset.fileName} deleted.`, 'OK', { duration: 4000 });
    } catch (e) {
      this.snackBar.open(problemMessage(toProblem(e)), 'OK', { duration: 6000 });
    }
  }

  private async upload(files: File[]): Promise<void> {
    const brandId = this.brands.activeBrandId();
    if (!brandId || files.length === 0) {
      return;
    }
    const problems: string[] = [];
    const tags = parseTags(this.uploadTags);
    const valid = files.filter((file) => {
      if (!ACCEPTED.includes(file.type)) {
        problems.push(`${file.name}: upload a JPEG, PNG or WebP image.`);
        return false;
      }
      if (file.size > MAX_BYTES) {
        problems.push(`${file.name}: images can be at most 10 MB.`);
        return false;
      }
      return true;
    });

    let uploaded = 0;
    this.uploading.update((n) => n + valid.length);
    for (const file of valid) {
      try {
        const asset = await firstValueFrom(this.api.uploadMedia(brandId, file, tags));
        if (this.brands.activeBrandId() === brandId) {
          this.assets.update((list) => [asset, ...list]);
        }
        uploaded++;
      } catch (e) {
        problems.push(`${file.name}: ${problemMessage(toProblem(e))}`);
      } finally {
        this.uploading.update((n) => n - 1);
      }
    }
    this.messages.set(problems);
    if (uploaded > 0) {
      this.snackBar.open(`${uploaded} image${uploaded === 1 ? '' : 's'} uploaded.`, 'OK', { duration: 3000 });
    }
  }

  private async reload(): Promise<void> {
    const brandId = this.brands.activeBrandId();
    if (!brandId) {
      return;
    }
    this.loading.set(true);
    this.messages.set([]);
    try {
      const assets = await firstValueFrom(this.api.listMedia(brandId));
      if (this.brands.activeBrandId() === brandId) {
        this.assets.set(assets);
      }
    } catch (e) {
      this.messages.set([problemMessage(toProblem(e))]);
    } finally {
      this.loading.set(false);
    }
  }
}
