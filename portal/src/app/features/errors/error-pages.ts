import { ChangeDetectionStrategy, Component } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'ms-forbidden',
  imports: [RouterLink, MatButtonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h1 class="page-title">You don't have access to this page</h1>
    <p>Ask a Tenant Admin in your workspace if you need it.</p>
    <a mat-stroked-button routerLink="/dashboard">Go to dashboard</a>
  `,
  styles: `.page-title { font: var(--mat-sys-headline-small); }`,
})
export class Forbidden {}

@Component({
  selector: 'ms-not-found',
  imports: [RouterLink, MatButtonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <main class="not-found">
      <h1 class="page-title">Page not found</h1>
      <p>The page you're looking for doesn't exist or has moved.</p>
      <a mat-stroked-button routerLink="/">Go home</a>
    </main>
  `,
  styles: `
    .not-found { padding: 48px 16px; text-align: center; }
    .page-title { font: var(--mat-sys-headline-small); }
  `,
})
export class NotFound {}
