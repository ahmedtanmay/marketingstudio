import { ChangeDetectionStrategy, Component, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { FormArray, NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatChipInputEvent, MatChipsModule } from '@angular/material/chips';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatRadioModule } from '@angular/material/radio';
import { MatTooltipModule } from '@angular/material/tooltip';
import { BrandVoice, TONE_PRESETS, TonePreset } from '../../core/api/api.types';
import { FormState } from '../auth/form-state';

const MAX_GUIDELINES = 2000;
const MAX_EXAMPLES = 3;
const MAX_BANNED = 200;

/** BRD-2: tone, guidelines, example posts and banned words/topics. */
@Component({
  selector: 'ms-voice-form',
  imports: [
    ReactiveFormsModule, MatRadioModule, MatFormFieldModule, MatInputModule, MatChipsModule, MatIconModule, MatButtonModule,
    MatTooltipModule, MatProgressBarModule,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <form [formGroup]="form" (ngSubmit)="submit()" novalidate class="voice-form">
      @if (state.error()) {
        <p class="form-error" role="alert">{{ state.error() }}</p>
      }
      @for (message of serverMessages(); track message) {
        <p class="form-error" role="alert">{{ message }}</p>
      }

      <fieldset>
        <legend>Tone</legend>
        <mat-radio-group formControlName="tonePreset" aria-label="Tone" class="tones">
          @for (tone of tones; track tone) {
            <mat-radio-button [value]="tone">{{ tone }}</mat-radio-button>
          }
        </mat-radio-group>
      </fieldset>

      <mat-form-field appearance="outline">
        <mat-label>Voice guidelines</mat-label>
        <textarea matInput formControlName="guidelines" rows="4" [maxlength]="maxGuidelines" data-testid="voice-guidelines"
                  placeholder="e.g. Warm and upbeat, short sentences, no slang, always mention fresh roasting"></textarea>
        <mat-hint align="end">{{ form.controls.guidelines.value.length }} / {{ maxGuidelines }}</mat-hint>
      </mat-form-field>

      <fieldset formArrayName="examplePosts">
        <legend>Example posts <span class="muted">(up to {{ maxExamples }})</span></legend>
        @for (control of examples.controls; track $index) {
          <div class="example">
            <mat-form-field appearance="outline">
              <mat-label>Example {{ $index + 1 }}</mat-label>
              <textarea matInput [formControlName]="$index" rows="2" maxlength="2200"></textarea>
            </mat-form-field>
            @if (!readOnly()) {
              <button mat-icon-button type="button" (click)="examples.removeAt($index)" [attr.aria-label]="'Remove example ' + ($index + 1)">
                <mat-icon>delete</mat-icon>
              </button>
            }
          </div>
        }
        @if (!readOnly() && examples.length < maxExamples) {
          <button mat-stroked-button type="button" (click)="addExample()">
            <mat-icon>add</mat-icon> Add example
          </button>
        }
      </fieldset>

      <mat-form-field appearance="outline">
        <mat-label>Banned words and topics</mat-label>
        <mat-chip-grid #bannedGrid aria-label="Banned words and topics" [disabled]="readOnly()">
          @for (term of banned(); track term) {
            <mat-chip-row (removed)="removeBanned(term)" [editable]="false">
              {{ term }}
              <button matChipRemove [attr.aria-label]="'Remove ' + term"><mat-icon>cancel</mat-icon></button>
            </mat-chip-row>
          }
        </mat-chip-grid>
        <!-- Outside the grid: an input is not a valid child of role="grid" (axe aria-required-children). -->
        <input placeholder="Type and press Enter" [matChipInputFor]="bannedGrid" (matChipInputTokenEnd)="addBanned($event)"
               data-testid="voice-banned" maxlength="60" aria-label="Add a banned word or topic" />
        <mat-hint>Agents will never use these ({{ banned().length }} / {{ maxBanned }})</mat-hint>
      </mat-form-field>

      @if (!readOnly()) {
        <div class="actions">
          <button mat-stroked-button type="button" disabled matTooltip="Available when the AI agents arrive (Sprint 5)">
            Preview voice
          </button>
          <button mat-flat-button type="submit" [disabled]="state.busy()" data-testid="voice-save">Save voice</button>
        </div>
      }
      @if (state.busy()) {
        <mat-progress-bar mode="indeterminate" />
      }
    </form>
  `,
  styles: `
    .voice-form { max-width: 720px; }
    fieldset { border: 0; padding: 0; margin: 0 0 16px; display: grid; gap: 8px; }
    legend { font: var(--mat-sys-title-small); margin-bottom: 8px; }
    .tones { display: flex; flex-wrap: wrap; gap: 8px 16px; }
    .example { display: flex; gap: 8px; align-items: flex-start; }
    .example mat-form-field { flex: 1; }
    .actions { display: flex; justify-content: flex-end; gap: 8px; }
    .muted { color: var(--mat-sys-on-surface-variant); font: var(--mat-sys-body-small); }
  `,
})
export class VoiceForm implements OnInit {
  readonly initial = input.required<BrandVoice>();
  readonly readOnly = input(false);
  readonly save = input.required<(voice: BrandVoice) => Promise<unknown>>();
  readonly saved = output<void>();

  private readonly fb = inject(NonNullableFormBuilder);
  protected readonly state = new FormState();
  protected readonly tones = TONE_PRESETS;
  protected readonly maxGuidelines = MAX_GUIDELINES;
  protected readonly maxExamples = MAX_EXAMPLES;
  protected readonly maxBanned = MAX_BANNED;
  protected readonly banned = signal<string[]>([]);
  protected readonly serverMessages = computed(() => Object.values(this.state.fields()));
  protected readonly form = this.fb.group({
    tonePreset: ['Friendly' as TonePreset, Validators.required],
    guidelines: ['', Validators.maxLength(MAX_GUIDELINES)],
    examplePosts: this.fb.array<string>([]),
  });

  protected get examples(): FormArray {
    return this.form.controls.examplePosts;
  }

  ngOnInit(): void {
    const voice = this.initial();
    this.form.patchValue({ tonePreset: voice.tonePreset, guidelines: voice.guidelines });
    voice.examplePosts.forEach((post) => this.examples.push(this.fb.control(post, Validators.required)));
    this.banned.set([...voice.bannedTerms]);
    if (this.readOnly()) {
      this.form.disable();
    }
  }

  protected addExample(): void {
    this.examples.push(this.fb.control('', Validators.required));
  }

  protected addBanned(event: MatChipInputEvent): void {
    const term = event.value.trim();
    if (term && this.banned().length < MAX_BANNED && !this.banned().some((t) => t.toLowerCase() === term.toLowerCase())) {
      this.banned.update((terms) => [...terms, term]);
      this.form.markAsDirty();
    }
    event.chipInput.clear();
  }

  protected removeBanned(term: string): void {
    this.banned.update((terms) => terms.filter((t) => t !== term));
    this.form.markAsDirty();
  }

  protected async submit(): Promise<void> {
    const v = this.form.getRawValue();
    const voice: BrandVoice = {
      tonePreset: v.tonePreset,
      guidelines: v.guidelines.trim(),
      examplePosts: v.examplePosts.map((p) => p.trim()).filter((p) => p.length > 0),
      bannedTerms: this.banned(),
    };
    if (await this.state.submit(this.form, () => this.save()(voice))) {
      this.saved.emit();
    }
  }
}
