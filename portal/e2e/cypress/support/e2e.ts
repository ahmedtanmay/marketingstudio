import 'cypress-axe';

export const PASSWORD = 'violet-harbour-lantern';

export const newEmail = (label: string) =>
  `${label}-${Date.now()}-${Math.floor(Math.random() * 1e6)}@example.com`;

declare global {
  // eslint-disable-next-line @typescript-eslint/no-namespace
  namespace Cypress {
    interface Chainable {
      /** Signs up through the UI and lands on the dashboard. */
      signUp(email: string, company?: string, name?: string): Chainable<void>;
      /** Signs in through the UI. */
      signIn(email: string, password?: string): Chainable<void>;
      /** Path + query of the first portal link in the newest email to `to` whose subject contains `subject`. */
      emailLink(to: string, subject: string): Chainable<string>;
      /** axe-core WCAG 2.1 A/AA check of the current page (Test Strategy.md → Accessibility). */
      checkPageA11y(): Chainable<void>;
    }
  }
}

Cypress.Commands.add('signUp', (email: string, company = 'E2E Coffee', name = 'Ana Admin') => {
  cy.visit('/signup');
  cy.get('[data-testid=name]').type(name);
  cy.get('[data-testid=company]').type(company);
  cy.get('[data-testid=email]').type(email);
  cy.get('[data-testid=password]').type(PASSWORD, { log: false });
  cy.get('[data-testid=submit]').click();
  cy.location('pathname').should('eq', '/dashboard');
});

Cypress.Commands.add('signIn', (email: string, password = PASSWORD) => {
  cy.visit('/login');
  cy.get('[data-testid=email]').type(email);
  cy.get('[data-testid=password]').type(password, { log: false });
  cy.get('[data-testid=submit]').click();
});

Cypress.Commands.add('emailLink', (to: string, subject: string) => {
  const mailpit = Cypress.env('mailpitUrl') as string;
  const find = (attempt: number): Cypress.Chainable<string> =>
    cy
      .request(`${mailpit}/api/v1/search?query=${encodeURIComponent(`to:"${to}"`)}`)
      .then((response): Cypress.Chainable<string> => {
        const message = (response.body.messages as { ID: string; Subject: string }[]).find((m) =>
          m.Subject.toLowerCase().includes(subject.toLowerCase()),
        );
        if (!message) {
          if (attempt > 20) {
            throw new Error(`No email to ${to} with subject containing "${subject}"`);
          }
          return cy.wait(500).then(() => find(attempt + 1));
        }
        return cy.request(`${mailpit}/api/v1/message/${message.ID}`).then((full) => {
          const link = (full.body.Text as string).match(/https?:\/\/\S+/)?.[0];
          if (!link) {
            throw new Error('Email has no link');
          }
          const url = new URL(link);
          return url.pathname + url.search;
        });
      });
  return find(0);
});

Cypress.Commands.add('checkPageA11y', () => {
  cy.injectAxe();
  cy.checkA11y(
    // Transient Material snackbars keep an aria-hidden copy of their text for the live-region announcement,
    // which axe reports as aria-hidden-focus. They are announced correctly; exclude only them.
    { exclude: ['.mat-mdc-snack-bar-container'] },
    { runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'] } },
    (violations) => {
      const summary = violations
        .map((v) => `${v.impact}: ${v.id} — ${v.help} → ${v.nodes.map((n) => n.target.join(' ')).join(' | ')}`)
        .join('\n');
      cy.task('log', `axe violations:\n${summary}`);
    },
  );
});
