import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { MatCardModule } from '@angular/material/card';

/** Centered card used by every signed-out page. */
@Component({
  selector: 'ms-auth-layout',
  imports: [MatCardModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <main class="auth-page">
      <div class="brand" aria-hidden="true">AI Marketing Studio</div>
      <mat-card appearance="outlined" class="auth-card">
        <mat-card-header>
          <mat-card-title><h1>{{ heading() }}</h1></mat-card-title>
          @if (subheading()) {
            <mat-card-subtitle>{{ subheading() }}</mat-card-subtitle>
          }
        </mat-card-header>
        <mat-card-content>
          <ng-content />
        </mat-card-content>
      </mat-card>
      <ng-content select="[footer]" />
    </main>
  `,
  styles: `
    .auth-page {
      min-height: 100dvh;
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      gap: 16px;
      padding: 24px 16px;
      box-sizing: border-box;
    }
    .brand {
      font: var(--mat-sys-title-large);
      color: var(--mat-sys-primary);
    }
    .auth-card {
      width: 100%;
      max-width: 420px;
    }
    h1 {
      font: var(--mat-sys-headline-small);
      margin: 0;
    }
  `,
})
export class AuthLayout {
  readonly heading = input.required<string>();
  readonly subheading = input<string>();
}
