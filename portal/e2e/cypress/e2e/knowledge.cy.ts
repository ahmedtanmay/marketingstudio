import { newEmail } from '../support/e2e';

const POLICY = `# Returns Policy

We accept returns within 30 days of delivery for unopened coffee bags.

## International orders

International customers pay return shipping. International refunds are issued within 14 days of receiving the parcel.
`;

describe('Knowledge base', () => {
  it('[KB-1] [KB-2] [KB-3] [KB-4] [TEN-1] uploads a document, adds an FAQ and finds both in test search', () => {
    cy.signUp(newEmail('kb-owner'), 'Knowledge Journey Co');
    cy.visit('/brands/new');
    cy.get('[data-testid=brand-name]').type('Knowing Beans');
    cy.get('[data-testid=brand-industry]').type('Food & Beverage');
    cy.get('[data-testid=brand-save]').click();
    cy.location('pathname').should('eq', '/brand/profile');

    // KB-1: upload; the status updates from processing to ready without a reload.
    cy.contains('a', 'Knowledge').click();
    cy.contains('No documents yet');
    cy.checkPageA11y();
    cy.get('[data-testid=kb-input]').selectFile(
      { contents: Cypress.Buffer.from(POLICY), fileName: 'returns-policy.md', mimeType: 'text/markdown' },
      { force: true },
    );
    cy.contains('1 document uploaded');
    cy.get('[data-testid=kb-table]').contains('tr', 'returns-policy.md').find('[data-status]', { timeout: 60_000 })
      .should('have.attr', 'data-status', 'Ready');
    cy.get('[data-testid=kb-usage]').should('contain.text', '1 / ');
    cy.checkPageA11y();

    // KB-3: FAQ.
    cy.contains('[role=tab]', 'FAQs').click();
    cy.get('[data-testid=faq-question]').type('Do you deliver on Sundays?');
    cy.get('[data-testid=faq-answer]').type('Yes, we deliver on Sundays from 8am to 8pm.');
    cy.get('[data-testid=faq-save]').click();
    cy.contains('FAQ added');
    cy.get('[data-testid=faq-list]').should('contain.text', 'Do you deliver on Sundays?');
    cy.checkPageA11y();

    // KB-4: test search shows what agents would get.
    cy.contains('[role=tab]', 'Test search').click();
    cy.get('[data-testid=kb-search]').type('international refunds');
    cy.get('[data-testid=kb-search-button]').click();
    cy.get('[data-testid=kb-hits]', { timeout: 20_000 }).should('contain.text', 'returns-policy.md').and('contain.text', 'International orders');

    cy.get('[data-testid=kb-search]').clear().type('deliver on Sundays');
    cy.get('[data-testid=kb-search-button]').click();
    cy.get('[data-testid=kb-hits]', { timeout: 20_000 }).should('contain.text', 'FAQ');

    cy.get('[data-testid=kb-search]').clear().type('zebra quantum xylophone');
    cy.get('[data-testid=kb-search-button]').click();
    cy.get('[data-testid=kb-no-match]').should('be.visible');
    cy.checkPageA11y();

    // TEN-1: the knowledge step ticks itself.
    cy.contains('a', 'Dashboard').click();
    cy.get('[data-testid=step-knowledge]').should('have.attr', 'data-done', 'true');
  });
});
