import { CurrencyPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, effect, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTableModule } from '@angular/material/table';
import { debounceTime, distinctUntilChanged, firstValueFrom } from 'rxjs';
import { Permission, Product } from '../../core/api/api.types';
import { problemMessage, toProblem } from '../../core/api/problem';
import { AuthStore } from '../../core/auth/auth.store';
import { BrandApi } from '../../core/brand/brand-api';
import { BrandStore } from '../../core/brand/brand.store';
import { NoBrand } from './brand-pages';
import { ProductDialog, ProductDialogData } from './product-dialog';

const PAGE_SIZE = 25;

/** PRD-1: products of the active brand — search, category filter, 25 per page. */
@Component({
  selector: 'ms-products-page',
  imports: [
    CurrencyPipe, ReactiveFormsModule, MatTableModule, MatPaginatorModule, MatFormFieldModule, MatInputModule, MatSelectModule,
    MatButtonModule, MatIconModule, MatProgressBarModule, NoBrand,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (!brands.activeBrandId()) {
      <ms-no-brand />
    } @else {
      <div class="header">
        <h1 class="page-title">Products</h1>
        @if (canWrite()) {
          <button mat-flat-button (click)="edit(null)" data-testid="add-product"><mat-icon>add</mat-icon> Add product</button>
        }
      </div>
      <div class="filters">
        <mat-form-field appearance="outline">
          <mat-label>Search by name</mat-label>
          <mat-icon matPrefix>search</mat-icon>
          <input matInput [formControl]="search" data-testid="product-search" />
        </mat-form-field>
        <mat-form-field appearance="outline">
          <mat-label>Category</mat-label>
          <mat-select [formControl]="category">
            <mat-option [value]="''">All categories</mat-option>
            @for (c of categories(); track c) {
              <mat-option [value]="c">{{ c }}</mat-option>
            }
          </mat-select>
        </mat-form-field>
      </div>
      @if (loading()) {
        <mat-progress-bar mode="indeterminate" />
      }
      @if (error()) {
        <p class="form-error" role="alert">{{ error() }}</p>
      }
      @if (!loading() && total() === 0) {
        <p class="empty" role="status">
          {{ search.value || category.value ? 'No products match.' : 'No products yet. Add the products agents should know about.' }}
        </p>
      } @else {
        <table mat-table [dataSource]="products()" class="table" data-testid="products-table">
          <ng-container matColumnDef="name">
            <th mat-header-cell *matHeaderCellDef>Name</th>
            <td mat-cell *matCellDef="let p">{{ p.name }}</td>
          </ng-container>
          <ng-container matColumnDef="category">
            <th mat-header-cell *matHeaderCellDef>Category</th>
            <td mat-cell *matCellDef="let p">{{ p.category ?? '—' }}</td>
          </ng-container>
          <ng-container matColumnDef="price">
            <th mat-header-cell *matHeaderCellDef>Price</th>
            <td mat-cell *matCellDef="let p">{{ p.price === null ? '—' : (p.price | currency: p.currency) }}</td>
          </ng-container>
          <ng-container matColumnDef="actions">
            <th mat-header-cell *matHeaderCellDef><span class="sr-only">Actions</span></th>
            <td mat-cell *matCellDef="let p">
              @if (canWrite()) {
                <button mat-icon-button (click)="edit(p)" [attr.aria-label]="'Edit ' + p.name"><mat-icon>edit</mat-icon></button>
                <button mat-icon-button (click)="remove(p)" [attr.aria-label]="'Delete ' + p.name"><mat-icon>delete</mat-icon></button>
              }
            </td>
          </ng-container>
          <tr mat-header-row *matHeaderRowDef="columns"></tr>
          <tr mat-row *matRowDef="let row; columns: columns"></tr>
        </table>
        <mat-paginator [length]="total()" [pageIndex]="page() - 1" [pageSize]="pageSize" [hidePageSize]="true"
                       (page)="changePage($event)" aria-label="Product pages" />
      }
    }
  `,
  styles: `
    .header { display: flex; justify-content: space-between; align-items: center; gap: 16px; margin-bottom: 16px; }
    .page-title { font: var(--mat-sys-headline-medium); margin: 0; }
    .filters { display: flex; gap: 16px; flex-wrap: wrap; }
    .filters mat-form-field { flex: 1 1 240px; }
    .table { width: 100%; }
    .empty { padding: 24px 0; color: var(--mat-sys-on-surface-variant); }
    :host { display: block; overflow-x: auto; }
  `,
})
export class ProductsPage {
  protected readonly brands = inject(BrandStore);
  private readonly auth = inject(AuthStore);
  private readonly api = inject(BrandApi);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);

  protected readonly search = new FormControl('', { nonNullable: true });
  protected readonly category = new FormControl('', { nonNullable: true });
  protected readonly products = signal<Product[]>([]);
  protected readonly categories = signal<string[]>([]);
  protected readonly total = signal(0);
  protected readonly page = signal(1);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly pageSize = PAGE_SIZE;
  protected readonly columns = ['name', 'category', 'price', 'actions'];
  protected readonly canWrite = () => this.auth.can(Permission.ProductsWrite);

  constructor() {
    effect(() => {
      const brandId = this.brands.activeBrandId();
      if (brandId) {
        this.page.set(1);
        void this.reload();
      }
    });

    const destroyRef = inject(DestroyRef);
    this.search.valueChanges.pipe(debounceTime(300), distinctUntilChanged(), takeUntilDestroyed(destroyRef)).subscribe(() => {
      this.page.set(1);
      void this.reload();
    });
    this.category.valueChanges.pipe(takeUntilDestroyed(destroyRef)).subscribe(() => {
      this.page.set(1);
      void this.reload();
    });
  }

  protected changePage(event: PageEvent): void {
    this.page.set(event.pageIndex + 1);
    void this.reload();
  }

  protected async edit(product: Product | null): Promise<void> {
    const data: ProductDialogData = { brandId: this.brands.activeBrandId()!, product };
    const saved = await firstValueFrom(this.dialog.open(ProductDialog, { data, maxWidth: '720px' }).afterClosed());
    if (saved) {
      this.snackBar.open(product ? 'Product saved.' : 'Product added.', 'OK', { duration: 4000 });
      await this.reload();
    }
  }

  protected async remove(product: Product): Promise<void> {
    if (!confirm(`Delete ${product.name}?`)) {
      return;
    }
    try {
      await firstValueFrom(this.api.deleteProduct(product.brandId, product.id), { defaultValue: undefined });
      this.snackBar.open(`${product.name} deleted.`, 'OK', { duration: 4000 });
    } catch (e) {
      this.snackBar.open(problemMessage(toProblem(e)), 'OK', { duration: 6000 });
    }
    await this.reload();
  }

  private async reload(): Promise<void> {
    const brandId = this.brands.activeBrandId();
    if (!brandId) {
      return;
    }
    this.loading.set(true);
    this.error.set(null);
    try {
      const [result, categories] = await Promise.all([
        firstValueFrom(this.api.searchProducts(brandId, {
          search: this.search.value.trim() || undefined,
          category: this.category.value || undefined,
          page: this.page(),
          pageSize: PAGE_SIZE,
        })),
        firstValueFrom(this.api.productCategories(brandId)),
      ]);
      if (this.brands.activeBrandId() === brandId) {
        this.products.set(result.items);
        this.total.set(result.total);
        this.categories.set(categories);
      }
    } catch (e) {
      this.error.set(problemMessage(toProblem(e)));
    } finally {
      this.loading.set(false);
    }
  }
}
