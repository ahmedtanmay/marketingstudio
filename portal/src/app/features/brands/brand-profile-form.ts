import { ChangeDetectionStrategy, Component, OnInit, computed, inject, input, output } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { startWith } from 'rxjs';
import { BrandProfileInput, INDUSTRIES, LANGUAGES } from '../../core/api/api.types';
import { FormState } from '../auth/form-state';

const TIME_ZONES: string[] = (() => {
  try {
    return Intl.supportedValuesOf('timeZone');
  } catch {
    return ['UTC', 'Asia/Kolkata', 'Europe/London', 'America/New_York'];
  }
})();

const browserTimeZone = (): string => {
  try {
    return Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC';
  } catch {
    return 'UTC';
  }
};

function filter(options: readonly string[], text: string): string[] {
  const needle = text.trim().toLowerCase();
  return needle ? options.filter((o) => o.toLowerCase().includes(needle)) : [...options];
}

/** BRD-1: brand profile fields, shared by "Create brand" and the profile tab. */
@Component({
  selector: 'ms-brand-profile-form',
  imports: [ReactiveFormsModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatAutocompleteModule, MatButtonModule, MatProgressBarModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <form [formGroup]="form" (ngSubmit)="submit()" novalidate class="profile-form">
      @if (state.error()) {
        <p class="form-error" role="alert">{{ state.error() }}</p>
      }
      @for (message of serverMessages(); track message) {
        <p class="form-error" role="alert">{{ message }}</p>
      }
      <mat-form-field appearance="outline">
        <mat-label>Brand name</mat-label>
        <input matInput formControlName="name" required maxlength="80" data-testid="brand-name" />
        @if (form.controls.name.invalid) {
          <mat-error>Enter a brand name</mat-error>
        }
      </mat-form-field>

      <mat-form-field appearance="outline">
        <mat-label>Industry</mat-label>
        <input matInput formControlName="industry" [matAutocomplete]="industryList" required data-testid="brand-industry" />
        <mat-autocomplete #industryList="matAutocomplete">
          @for (industry of industryOptions(); track industry) {
            <mat-option [value]="industry">{{ industry }}</mat-option>
          }
        </mat-autocomplete>
        @if (form.controls.industry.invalid) {
          <mat-error>Enter an industry</mat-error>
        }
      </mat-form-field>

      <div class="two-col">
        <mat-form-field appearance="outline">
          <mat-label>Languages</mat-label>
          <mat-select formControlName="languages" multiple required data-testid="brand-languages">
            @for (language of languages; track language.code) {
              <mat-option [value]="language.code">{{ language.name }}</mat-option>
            }
          </mat-select>
          <mat-hint>Languages the agents may write in</mat-hint>
          @if (form.controls.languages.invalid) {
            <mat-error>Choose at least one language</mat-error>
          }
        </mat-form-field>

        <mat-form-field appearance="outline">
          <mat-label>Time zone</mat-label>
          <input matInput formControlName="timezone" [matAutocomplete]="zoneList" required data-testid="brand-timezone" />
          <mat-autocomplete #zoneList="matAutocomplete">
            @for (zone of zoneOptions(); track zone) {
              <mat-option [value]="zone">{{ zone }}</mat-option>
            }
          </mat-autocomplete>
          <mat-hint>Used to schedule posts</mat-hint>
        </mat-form-field>
      </div>

      <mat-form-field appearance="outline">
        <mat-label>Description (optional)</mat-label>
        <textarea matInput formControlName="description" rows="2" maxlength="1000"></textarea>
      </mat-form-field>

      <mat-form-field appearance="outline">
        <mat-label>Target audience (optional)</mat-label>
        <textarea matInput formControlName="targetAudience" rows="2" maxlength="1000"
                  placeholder="e.g. Urban professionals aged 25–40 who love speciality coffee"></textarea>
      </mat-form-field>

      <mat-form-field appearance="outline">
        <mat-label>Website (optional)</mat-label>
        <input matInput type="url" formControlName="websiteUrl" placeholder="https://" />
      </mat-form-field>

      @if (!readOnly()) {
        <div class="actions">
          <button mat-flat-button type="submit" [disabled]="state.busy()" data-testid="brand-save">{{ submitLabel() }}</button>
        </div>
      }
      @if (state.busy()) {
        <mat-progress-bar mode="indeterminate" />
      }
    </form>
  `,
  styles: `
    .profile-form { max-width: 720px; }
    .two-col { display: grid; grid-template-columns: 1fr 1fr; gap: 16px; }
    .actions { display: flex; justify-content: flex-end; }
    @media (max-width: 599px) { .two-col { grid-template-columns: 1fr; } }
  `,
})
export class BrandProfileForm implements OnInit {
  readonly initial = input<BrandProfileInput | null>(null);
  readonly submitLabel = input('Save');
  readonly readOnly = input(false);
  /** Persists the profile; throw to show the API's error in the form. */
  readonly save = input.required<(profile: BrandProfileInput) => Promise<unknown>>();
  readonly saved = output<void>();

  protected readonly state = new FormState();
  protected readonly languages = LANGUAGES;
  protected readonly form = inject(NonNullableFormBuilder).group({
    name: ['', [Validators.required, Validators.maxLength(80)]],
    industry: ['', [Validators.required, Validators.maxLength(80)]],
    languages: [['en'] as string[], Validators.required],
    timezone: [browserTimeZone(), Validators.required],
    description: [''],
    targetAudience: [''],
    websiteUrl: [''],
  });

  private readonly industryText = toSignal(this.form.controls.industry.valueChanges.pipe(startWith('')), { initialValue: '' });
  private readonly zoneText = toSignal(this.form.controls.timezone.valueChanges.pipe(startWith('')), { initialValue: '' });
  protected readonly industryOptions = computed(() => filter(INDUSTRIES, this.industryText()));
  protected readonly zoneOptions = computed(() => filter(TIME_ZONES, this.zoneText()).slice(0, 50));
  protected readonly serverMessages = computed(() => Object.values(this.state.fields()));

  ngOnInit(): void {
    const initial = this.initial();
    if (initial) {
      this.form.setValue({
        name: initial.name,
        industry: initial.industry,
        languages: initial.languages,
        timezone: initial.timezone,
        description: initial.description ?? '',
        targetAudience: initial.targetAudience ?? '',
        websiteUrl: initial.websiteUrl ?? '',
      });
    }
    if (this.readOnly()) {
      this.form.disable();
    }
  }

  protected async submit(): Promise<void> {
    const v = this.form.getRawValue();
    const profile: BrandProfileInput = {
      name: v.name.trim(),
      industry: v.industry.trim(),
      languages: v.languages,
      timezone: v.timezone.trim(),
      description: v.description.trim() || null,
      targetAudience: v.targetAudience.trim() || null,
      websiteUrl: v.websiteUrl.trim() || null,
    };
    if (await this.state.submit(this.form, () => this.save()(profile))) {
      this.saved.emit();
    }
  }
}
