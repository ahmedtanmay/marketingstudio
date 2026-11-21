import { DatePipe, DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTableModule } from '@angular/material/table';
import { MatTabsModule } from '@angular/material/tabs';
import { firstValueFrom } from 'rxjs';
import { Faq, KNOWLEDGE_EXTENSIONS, KnowledgeDocument, KnowledgeSearchResult, KnowledgeUsage, Permission } from '../../core/api/api.types';
import { problemMessage, toProblem } from '../../core/api/problem';
import { AuthStore } from '../../core/auth/auth.store';
import { BrandApi } from '../../core/brand/brand-api';
import { BrandStore } from '../../core/brand/brand.store';
import { NoBrand } from './brand-pages';

const POLL_MS = 2000;
const SOURCE_LABELS: Record<string, string> = { document: 'Document', faq: 'FAQ', product: 'Product' };

/** KB-1..4: documents (upload, status, retry, delete), FAQs, and a test search showing what agents would get. */
@Component({
  selector: 'ms-knowledge-page',
  imports: [
    DatePipe, DecimalPipe, FormsModule, MatTabsModule, MatTableModule, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule,
    MatProgressBarModule, MatProgressSpinnerModule, NoBrand,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (!brands.activeBrandId()) {
      <ms-no-brand />
    } @else {
      <h1 class="page-title">Knowledge base</h1>
      <p class="muted">Agents answer and write from these sources only. Keep them accurate.</p>

      <mat-tab-group>
        <!-- ================================================================ Documents -->
        <mat-tab [label]="'Documents (' + documents().length + ')'">
          <div class="tab">
            @if (usage(); as u) {
              <p class="muted" data-testid="kb-usage">
                {{ u.documents }} / {{ u.maxDocuments }} documents · {{ u.storageBytes / 1048576 | number: '1.0-1' }} MB of
                {{ u.maxStorageBytes / 1048576 | number: '1.0-0' }} MB
              </p>
            }
            @if (canWrite()) {
              <div class="dropzone" [class.dragging]="dragging()" (dragover)="onDragOver($event)" (dragleave)="dragging.set(false)"
                   (drop)="onDrop($event)">
                <mat-icon aria-hidden="true">upload_file</mat-icon>
                <p>Drag documents here or</p>
                <label class="upload-button">
                  <span>Choose files</span>
                  <input type="file" multiple [accept]="accept" (change)="onPick($event)" data-testid="kb-input" />
                </label>
                <p class="muted">PDF, Word (.docx), text, Markdown or HTML
                  @if (usage(); as u) { · up to {{ u.maxFileBytes / 1048576 }} MB each }</p>
              </div>
            }
            @for (message of uploadErrors(); track message) {
              <p class="form-error" role="alert">{{ message }}</p>
            }
            @if (loading()) {
              <mat-progress-bar mode="indeterminate" />
            }
            @if (documents().length === 0 && !loading()) {
              <p class="empty" role="status">No documents yet. Upload brand guidelines, product sheets or policies.</p>
            } @else {
              <table mat-table [dataSource]="documents()" class="table" data-testid="kb-table">
                <ng-container matColumnDef="name">
                  <th mat-header-cell *matHeaderCellDef>Name</th>
                  <td mat-cell *matCellDef="let d">{{ d.fileName }}</td>
                </ng-container>
                <ng-container matColumnDef="status">
                  <th mat-header-cell *matHeaderCellDef>Status</th>
                  <td mat-cell *matCellDef="let d" [attr.data-status]="d.status">
                    <span class="status" [class]="'status ' + d.status.toLowerCase()">
                      @switch (d.status) {
                        @case ('Ready') { <mat-icon aria-hidden="true">check_circle</mat-icon> Ready }
                        @case ('Failed') { <mat-icon aria-hidden="true">error</mat-icon> Failed }
                        @case ('Rejected') { <mat-icon aria-hidden="true">gpp_bad</mat-icon> Rejected }
                        @default { <mat-spinner diameter="16" aria-hidden="true" /> Processing… }
                      }
                    </span>
                    @if (d.failureReason) { <div class="reason">{{ d.failureReason }}</div> }
                  </td>
                </ng-container>
                <ng-container matColumnDef="chunks">
                  <th mat-header-cell *matHeaderCellDef>Sections</th>
                  <td mat-cell *matCellDef="let d">{{ d.status === 'Ready' ? d.chunkCount : '—' }}</td>
                </ng-container>
                <ng-container matColumnDef="uploaded">
                  <th mat-header-cell *matHeaderCellDef>Uploaded</th>
                  <td mat-cell *matCellDef="let d">{{ d.createdAt | date: 'mediumDate' }}</td>
                </ng-container>
                <ng-container matColumnDef="actions">
                  <th mat-header-cell *matHeaderCellDef><span class="sr-only">Actions</span></th>
                  <td mat-cell *matCellDef="let d">
                    @if (canWrite()) {
                      @if (d.status === 'Failed') {
                        <button mat-button (click)="retry(d)">Retry</button>
                      }
                      <button mat-icon-button (click)="removeDocument(d)" [attr.aria-label]="'Delete ' + d.fileName">
                        <mat-icon>delete</mat-icon>
                      </button>
                    }
                  </td>
                </ng-container>
                <tr mat-header-row *matHeaderRowDef="columns"></tr>
                <tr mat-row *matRowDef="let row; columns: columns"></tr>
              </table>
            }
          </div>
        </mat-tab>

        <!-- ================================================================ FAQs -->
        <mat-tab [label]="'FAQs (' + faqs().length + ')'">
          <div class="tab">
            @if (canWrite()) {
              <form class="faq-form" (ngSubmit)="saveFaq()" #faqForm="ngForm">
                <h2>{{ editingFaq() ? 'Edit FAQ' : 'Add an FAQ' }}</h2>
                <mat-form-field appearance="outline">
                  <mat-label>Question</mat-label>
                  <input matInput name="question" [(ngModel)]="question" required maxlength="500" data-testid="faq-question" />
                </mat-form-field>
                <mat-form-field appearance="outline">
                  <mat-label>Answer</mat-label>
                  <textarea matInput name="answer" [(ngModel)]="answer" required maxlength="4000" rows="3" data-testid="faq-answer"></textarea>
                </mat-form-field>
                @if (faqError()) {
                  <p class="form-error" role="alert">{{ faqError() }}</p>
                }
                <div class="actions">
                  @if (editingFaq()) {
                    <button mat-button type="button" (click)="cancelEdit()">Cancel</button>
                  }
                  <button mat-flat-button type="submit" [disabled]="!question.trim() || !answer.trim() || savingFaq()" data-testid="faq-save">
                    {{ editingFaq() ? 'Save' : 'Add FAQ' }}
                  </button>
                </div>
              </form>
            }
            @if (faqs().length === 0) {
              <p class="empty" role="status">No FAQs yet. Short question-and-answer pairs help the Support Agent most.</p>
            }
            <ul class="faq-list" data-testid="faq-list">
              @for (faq of faqs(); track faq.id) {
                <li>
                  <div class="q">{{ faq.question }}</div>
                  <div class="a">{{ faq.answer }}</div>
                  @if (canWrite()) {
                    <div class="actions">
                      <button mat-button (click)="editFaq(faq)">Edit</button>
                      <button mat-button (click)="removeFaq(faq)">Delete</button>
                    </div>
                  }
                </li>
              }
            </ul>
          </div>
        </mat-tab>

        <!-- ================================================================ Test search (KB-4) -->
        <mat-tab label="Test search">
          <div class="tab">
            <p class="muted">See exactly what the agents would find for a question.</p>
            <form class="search" (ngSubmit)="search()">
              <mat-form-field appearance="outline">
                <mat-label>Ask a question</mat-label>
                <input matInput name="query" [(ngModel)]="query" maxlength="500" data-testid="kb-search" />
              </mat-form-field>
              <button mat-flat-button type="submit" [disabled]="!query.trim() || searching()" data-testid="kb-search-button">Search</button>
            </form>
            @if (searching()) {
              <mat-progress-bar mode="indeterminate" />
            }
            @if (result(); as r) {
              @if (r.lowConfidence) {
                <p class="empty" role="status" data-testid="kb-no-match">
                  No confident match. Agents would say they don't know — add a document or FAQ that answers this.
                </p>
              }
              <ol class="hits" data-testid="kb-hits">
                @for (hit of r.hits; track $index) {
                  <li>
                    <div class="hit-head">
                      <span class="source">{{ sourceLabel(hit.sourceType) }}</span>
                      <strong>{{ hit.title }}</strong>
                      @if (hit.headingPath) { <span class="muted"> › {{ hit.headingPath }}</span> }
                      <span class="score" [attr.aria-label]="'Relevance ' + hit.score">{{ hit.score | number: '1.2-2' }}</span>
                    </div>
                    <p class="hit-text">{{ hit.text }}</p>
                  </li>
                }
              </ol>
            }
          </div>
        </mat-tab>
      </mat-tab-group>
    }
  `,
  styles: `
    .page-title { font: var(--mat-sys-headline-medium); margin: 0 0 4px; }
    .muted { color: var(--mat-sys-on-surface-variant); }
    .tab { padding: 16px 0; }
    .dropzone { border: 2px dashed var(--mat-sys-outline-variant); border-radius: 12px; padding: 16px; text-align: center; margin-bottom: 16px; }
    .dropzone.dragging { border-color: var(--mat-sys-primary); background: var(--mat-sys-surface-container); }
    .dropzone mat-icon { color: var(--mat-sys-primary); }
    .dropzone p { margin: 4px 0; }
    .upload-button input { position: absolute; width: 1px; height: 1px; opacity: 0; }
    .upload-button span { cursor: pointer; display: inline-block; padding: 8px 16px; border: 1px solid var(--mat-sys-outline); border-radius: 20px; }
    .upload-button:focus-within span { outline: 2px solid var(--mat-sys-primary); }
    .table { width: 100%; }
    .status { display: inline-flex; align-items: center; gap: 4px; }
    .status.ready mat-icon { color: var(--mat-sys-primary); }
    .status.failed mat-icon, .status.rejected mat-icon { color: var(--mat-sys-error); }
    .reason { font: var(--mat-sys-body-small); color: var(--mat-sys-error); max-width: 360px; }
    .empty { padding: 16px 0; color: var(--mat-sys-on-surface-variant); }
    .faq-form { max-width: 720px; }
    .faq-form h2 { font: var(--mat-sys-title-medium); margin: 0 0 8px; }
    .actions { display: flex; justify-content: flex-end; gap: 8px; }
    .faq-list { list-style: none; padding: 0; display: grid; gap: 12px; max-width: 720px; }
    .faq-list li { border: 1px solid var(--mat-sys-outline-variant); border-radius: 12px; padding: 12px; }
    .q { font: var(--mat-sys-title-small); }
    .a { white-space: pre-line; margin-top: 4px; }
    .search { display: flex; gap: 12px; align-items: baseline; max-width: 720px; grid-template-columns: none; }
    .search mat-form-field { flex: 1; }
    .hits { padding-left: 20px; display: grid; gap: 12px; max-width: 840px; }
    .hit-head { display: flex; flex-wrap: wrap; align-items: baseline; gap: 6px; }
    .source { font: var(--mat-sys-label-small); padding: 2px 8px; border-radius: 8px; background: var(--mat-sys-secondary-container); }
    .score { margin-left: auto; font: var(--mat-sys-label-medium); color: var(--mat-sys-on-surface-variant); }
    .hit-text { margin: 4px 0 0; white-space: pre-line; }
    :host { display: block; overflow-x: auto; }
  `,
})
export class KnowledgePage {
  protected readonly brands = inject(BrandStore);
  private readonly auth = inject(AuthStore);
  private readonly api = inject(BrandApi);
  private readonly snackBar = inject(MatSnackBar);

  protected readonly accept = KNOWLEDGE_EXTENSIONS.join(',');
  protected readonly columns = ['name', 'status', 'chunks', 'uploaded', 'actions'];
  protected readonly documents = signal<KnowledgeDocument[]>([]);
  protected readonly usage = signal<KnowledgeUsage | null>(null);
  protected readonly faqs = signal<Faq[]>([]);
  protected readonly loading = signal(false);
  protected readonly dragging = signal(false);
  protected readonly uploadErrors = signal<string[]>([]);
  protected readonly editingFaq = signal<Faq | null>(null);
  protected readonly savingFaq = signal(false);
  protected readonly faqError = signal<string | null>(null);
  protected readonly searching = signal(false);
  protected readonly result = signal<KnowledgeSearchResult | null>(null);
  protected question = '';
  protected answer = '';
  protected query = '';
  protected readonly canWrite = () => this.auth.can(Permission.KnowledgeWrite);
  private readonly pending = computed(() => this.documents().some((d) => d.status === 'Uploaded' || d.status === 'Processing'));
  private pollTimer: ReturnType<typeof setTimeout> | undefined;

  constructor() {
    effect(() => {
      if (this.brands.activeBrandId()) {
        this.result.set(null);
        this.cancelEdit();
        void this.reload();
      }
    });
    // While anything is processing, refresh the list so statuses update without a page reload.
    effect(() => {
      clearTimeout(this.pollTimer);
      if (this.pending()) {
        this.pollTimer = setTimeout(() => void this.refreshDocuments(), POLL_MS);
      }
    });
    inject(DestroyRef).onDestroy(() => clearTimeout(this.pollTimer));
  }

  protected sourceLabel(type: string): string {
    return SOURCE_LABELS[type] ?? type;
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

  protected async retry(doc: KnowledgeDocument): Promise<void> {
    await this.run(() => firstValueFrom(this.api.retryDocument(doc.brandId, doc.id)), null);
    await this.refreshDocuments();
  }

  protected async removeDocument(doc: KnowledgeDocument): Promise<void> {
    if (confirm(`Delete ${doc.fileName}? Agents will stop using it immediately.`)) {
      await this.run(() => firstValueFrom(this.api.deleteDocument(doc.brandId, doc.id), { defaultValue: undefined }), `${doc.fileName} deleted.`);
      await this.reload();
    }
  }

  protected editFaq(faq: Faq): void {
    this.editingFaq.set(faq);
    this.question = faq.question;
    this.answer = faq.answer;
  }

  protected cancelEdit(): void {
    this.editingFaq.set(null);
    this.question = '';
    this.answer = '';
    this.faqError.set(null);
  }

  protected async saveFaq(): Promise<void> {
    const brandId = this.brands.activeBrandId()!;
    const body = { question: this.question.trim(), answer: this.answer.trim() };
    const editing = this.editingFaq();
    this.savingFaq.set(true);
    this.faqError.set(null);
    try {
      await firstValueFrom(editing ? this.api.updateFaq(brandId, editing.id, body) : this.api.createFaq(brandId, body));
      this.snackBar.open(editing ? 'FAQ saved.' : 'FAQ added.', 'OK', { duration: 3000 });
      this.cancelEdit();
      this.faqs.set(await firstValueFrom(this.api.listFaqs(brandId)));
    } catch (e) {
      this.faqError.set(problemMessage(toProblem(e)));
    } finally {
      this.savingFaq.set(false);
    }
  }

  protected async removeFaq(faq: Faq): Promise<void> {
    if (confirm('Delete this FAQ?')) {
      await this.run(() => firstValueFrom(this.api.deleteFaq(faq.brandId, faq.id), { defaultValue: undefined }), 'FAQ deleted.');
      this.faqs.set(await firstValueFrom(this.api.listFaqs(faq.brandId)));
    }
  }

  protected async search(): Promise<void> {
    const brandId = this.brands.activeBrandId();
    if (!brandId || !this.query.trim()) {
      return;
    }
    this.searching.set(true);
    try {
      this.result.set(await firstValueFrom(this.api.searchKnowledge(brandId, this.query.trim())));
    } catch (e) {
      this.snackBar.open(problemMessage(toProblem(e)), 'OK', { duration: 6000 });
    } finally {
      this.searching.set(false);
    }
  }

  private async upload(files: File[]): Promise<void> {
    const brandId = this.brands.activeBrandId();
    if (!brandId || files.length === 0) {
      return;
    }
    const problems: string[] = [];
    let uploaded = 0;
    for (const file of files) {
      const extension = file.name.slice(file.name.lastIndexOf('.')).toLowerCase();
      if (!KNOWLEDGE_EXTENSIONS.includes(extension)) {
        problems.push(`${file.name}: upload a PDF, Word (.docx), text, Markdown or HTML file.`);
        continue;
      }
      try {
        const doc = await firstValueFrom(this.api.uploadDocument(brandId, file));
        this.documents.update((list) => [doc, ...list]);
        uploaded++;
      } catch (e) {
        problems.push(`${file.name}: ${problemMessage(toProblem(e))}`);
      }
    }
    this.uploadErrors.set(problems);
    if (uploaded > 0) {
      this.snackBar.open(`${uploaded} document${uploaded === 1 ? '' : 's'} uploaded — processing…`, 'OK', { duration: 3000 });
      await this.refreshUsage(brandId);
    }
  }

  private async run(action: () => Promise<unknown>, success: string | null): Promise<void> {
    try {
      await action();
      if (success) {
        this.snackBar.open(success, 'OK', { duration: 3000 });
      }
    } catch (e) {
      this.snackBar.open(problemMessage(toProblem(e)), 'OK', { duration: 6000 });
    }
  }

  private async refreshDocuments(): Promise<void> {
    const brandId = this.brands.activeBrandId();
    if (!brandId) {
      return;
    }
    try {
      const docs = await firstValueFrom(this.api.listDocuments(brandId));
      if (this.brands.activeBrandId() === brandId) {
        this.documents.set(docs);
      }
    } catch {
      // Polling is best-effort; the next action reloads.
    }
  }

  private async refreshUsage(brandId: string): Promise<void> {
    try {
      this.usage.set(await firstValueFrom(this.api.knowledgeUsage(brandId)));
    } catch {
      this.usage.set(null);
    }
  }

  private async reload(): Promise<void> {
    const brandId = this.brands.activeBrandId();
    if (!brandId) {
      return;
    }
    this.loading.set(true);
    this.uploadErrors.set([]);
    try {
      const [docs, faqs] = await Promise.all([firstValueFrom(this.api.listDocuments(brandId)), firstValueFrom(this.api.listFaqs(brandId))]);
      if (this.brands.activeBrandId() === brandId) {
        this.documents.set(docs);
        this.faqs.set(faqs);
      }
      await this.refreshUsage(brandId);
    } catch (e) {
      this.uploadErrors.set([problemMessage(toProblem(e))]);
    } finally {
      this.loading.set(false);
    }
  }
}
