import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { firstValueFrom } from 'rxjs';
import { CURRENCIES, MediaAsset, Product, ProductInput } from '../../core/api/api.types';
import { BrandApi } from '../../core/brand/brand-api';
import { FormState } from '../auth/form-state';

export interface ProductDialogData {
  brandId: string;
  product: Product | null;
}

/** PRD-1: add or edit a product. Images are picked from the brand's media library. */
@Component({
  selector: 'ms-product-dialog',
  imports: [ReactiveFormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatCheckboxModule, MatProgressBarModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2 mat-dialog-title>{{ data.product ? 'Edit product' : 'Add product' }}</h2>
    <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
      <mat-dialog-content class="body">
        @if (state.error()) {
          <p class="form-error" role="alert">{{ state.error() }}</p>
        }
        @for (message of serverMessages(); track message) {
          <p class="form-error" role="alert">{{ message }}</p>
        }
        <mat-form-field appearance="outline">
          <mat-label>Name</mat-label>
          <input matInput formControlName="name" required maxlength="120" cdkFocusInitial data-testid="product-name" />
          @if (form.controls.name.invalid) {
            <mat-error>Enter a product name</mat-error>
          }
        </mat-form-field>
        <mat-form-field appearance="outline">
          <mat-label>Description</mat-label>
          <textarea matInput formControlName="description" rows="3" maxlength="4000"></textarea>
        </mat-form-field>
        <div class="row">
          <mat-form-field appearance="outline">
            <mat-label>Category</mat-label>
            <input matInput formControlName="category" maxlength="60" data-testid="product-category" />
          </mat-form-field>
          <mat-form-field appearance="outline" class="price">
            <mat-label>Price</mat-label>
            <input matInput type="number" min="0" step="0.01" formControlName="price" data-testid="product-price" />
          </mat-form-field>
          <mat-form-field appearance="outline" class="currency">
            <mat-label>Currency</mat-label>
            <mat-select formControlName="currency">
              @for (code of currencies; track code) {
                <mat-option [value]="code">{{ code }}</mat-option>
              }
            </mat-select>
          </mat-form-field>
        </div>
        <mat-form-field appearance="outline">
          <mat-label>Key features</mat-label>
          <textarea matInput formControlName="features" rows="3" placeholder="One per line"></textarea>
          <mat-hint>One feature per line (up to 20). Agents only claim features listed here.</mat-hint>
        </mat-form-field>
        <mat-form-field appearance="outline">
          <mat-label>Product page</mat-label>
          <input matInput type="url" formControlName="url" placeholder="https://" />
        </mat-form-field>

        <fieldset class="images">
          <legend>Images</legend>
          @if (media().length === 0) {
            <p class="muted">No images in this brand's media library yet. Upload them under Media.</p>
          }
          <div class="thumbs">
            @for (asset of media(); track asset.id) {
              <label class="thumb" [class.selected]="selected().has(asset.id)">
                <img [src]="asset.url" [alt]="asset.fileName" loading="lazy" />
                <mat-checkbox [checked]="selected().has(asset.id)" (change)="toggle(asset.id)"
                              [aria-label]="'Use ' + asset.fileName"></mat-checkbox>
              </label>
            }
          </div>
        </fieldset>
        @if (state.busy()) {
          <mat-progress-bar mode="indeterminate" />
        }
      </mat-dialog-content>
      <mat-dialog-actions align="end">
        <button mat-button type="button" mat-dialog-close>Cancel</button>
        <button mat-flat-button type="submit" [disabled]="state.busy()" data-testid="product-save">Save</button>
      </mat-dialog-actions>
    </form>
  `,
  styles: `
    .body { display: grid; gap: 4px; min-width: min(560px, 80vw); }
    .row { display: grid; grid-template-columns: 2fr 1fr 1fr; gap: 12px; }
    fieldset { border: 0; padding: 0; margin: 0; }
    legend { font: var(--mat-sys-title-small); margin-bottom: 8px; }
    .thumbs { display: flex; flex-wrap: wrap; gap: 8px; }
    .thumb { position: relative; width: 88px; height: 88px; border: 2px solid transparent; border-radius: 8px; overflow: hidden; cursor: pointer; }
    .thumb.selected { border-color: var(--mat-sys-primary); }
    .thumb img { width: 100%; height: 100%; object-fit: cover; }
    .thumb mat-checkbox { position: absolute; top: -4px; left: -4px; }
    .muted { color: var(--mat-sys-on-surface-variant); }
    @media (max-width: 599px) { .row { grid-template-columns: 1fr; } }
  `,
})
export class ProductDialog implements OnInit {
  protected readonly data = inject<ProductDialogData>(MAT_DIALOG_DATA);
  private readonly api = inject(BrandApi);
  private readonly dialogRef = inject(MatDialogRef<ProductDialog, boolean>);
  protected readonly state = new FormState();
  protected readonly currencies = CURRENCIES;
  protected readonly media = signal<MediaAsset[]>([]);
  protected readonly selected = signal(new Set<string>());
  protected readonly serverMessages = computed(() => Object.values(this.state.fields()));
  protected readonly form = inject(NonNullableFormBuilder).group({
    name: ['', [Validators.required, Validators.maxLength(120)]],
    description: [''],
    category: [''],
    price: [null as number | null, Validators.min(0)],
    currency: ['INR'],
    features: [''],
    url: [''],
  });

  async ngOnInit(): Promise<void> {
    const product = this.data.product;
    if (product) {
      this.form.setValue({
        name: product.name,
        description: product.description ?? '',
        category: product.category ?? '',
        price: product.price,
        currency: product.currency ?? 'INR',
        features: product.features.join('\n'),
        url: product.url ?? '',
      });
      this.selected.set(new Set(product.imageAssetIds));
    }
    try {
      this.media.set(await firstValueFrom(this.api.listMedia(this.data.brandId)));
    } catch {
      this.media.set([]);
    }
  }

  protected toggle(assetId: string): void {
    this.selected.update((current) => {
      const next = new Set(current);
      if (!next.delete(assetId)) {
        next.add(assetId);
      }
      return next;
    });
  }

  protected async submit(): Promise<void> {
    const v = this.form.getRawValue();
    const hasPrice = v.price !== null && `${v.price}` !== '';
    const input: ProductInput = {
      name: v.name.trim(),
      description: v.description.trim() || null,
      category: v.category.trim() || null,
      price: hasPrice ? Number(v.price) : null,
      currency: hasPrice ? v.currency : null,
      features: v.features.split('\n').map((f) => f.trim()).filter((f) => f.length > 0),
      url: v.url.trim() || null,
      imageAssetIds: [...this.selected()],
    };
    const product = this.data.product;
    const ok = await this.state.submit(this.form, () =>
      firstValueFrom(
        product
          ? this.api.updateProduct(this.data.brandId, product.id, input)
          : this.api.createProduct(this.data.brandId, input),
      ),
    );
    if (ok) {
      this.dialogRef.close(true);
    }
  }
}
