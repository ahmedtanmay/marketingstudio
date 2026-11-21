import { newEmail } from '../support/e2e';

/** A valid 1×1 PNG. */
const PNG_BASE64 = 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==';

describe('Brands, products and media', () => {
  it('[BRD-1] [BRD-2] [BRD-3] [PRD-1] [TEN-1] sets up a brand end to end and the checklist follows', () => {
    cy.signUp(newEmail('brand-owner'), 'Brand Journey Co');

    // TEN-1: brand and product steps start open.
    cy.get('[data-testid=step-brand]').should('have.attr', 'data-done', 'false');
    cy.get('[data-testid=step-products]').should('have.attr', 'data-done', 'false');
    cy.checkPageA11y();

    // BRD-1: create the first brand from the checklist.
    cy.get('[data-testid=step-brand] a').click();
    cy.location('pathname').should('eq', '/brands/new');
    cy.get('[data-testid=brand-name]').type('Roast & Co');
    cy.get('[data-testid=brand-industry]').type('Food & Beverage');
    cy.get('[data-testid=brand-timezone]').clear().type('Asia/Kolkata');
    cy.checkPageA11y();
    cy.get('[data-testid=brand-save]').click();
    cy.location('pathname').should('eq', '/brand/profile');
    cy.contains('h1', 'Roast & Co');
    cy.get('[data-testid=brand-switcher]').should('contain.text', 'Roast & Co');

    // BRD-2: voice.
    cy.contains('[role=tab]', 'Voice').click();
    cy.get('[data-testid=voice-guidelines]').type('Warm, short sentences. Always mention fresh roasting.');
    cy.get('[data-testid=voice-banned]').type('cheap{enter}');
    cy.checkPageA11y();
    cy.get('[data-testid=voice-save]').click();
    cy.contains('Voice saved');

    // BRD-3: upload an image to the media library; it renders through its signed link.
    cy.contains('a', 'Media').click();
    cy.get('[data-testid=media-input]').selectFile(
      { contents: Cypress.Buffer.from(PNG_BASE64, 'base64'), fileName: 'logo.png', mimeType: 'image/png' },
      { force: true },
    );
    cy.contains('1 image uploaded');
    cy.get('[data-testid=media-grid] img')
      .should('have.length', 1)
      .and(($img) => expect(($img[0] as HTMLImageElement).naturalWidth).to.be.greaterThan(0));
    cy.checkPageA11y();

    // PRD-1: add a product that uses the image; search finds it.
    cy.contains('a', 'Products').click();
    cy.get('[data-testid=add-product]').click();
    cy.get('[data-testid=product-name]').type('Cold Brew Concentrate');
    cy.get('[data-testid=product-category]').type('Coffee');
    cy.get('[data-testid=product-price]').type('349.50');
    cy.get('mat-checkbox input').first().check({ force: true });
    cy.checkPageA11y();
    cy.get('[data-testid=product-save]').click();
    cy.contains('Product added');
    cy.get('[data-testid=products-table]').should('contain.text', 'Cold Brew Concentrate').and('contain.text', '349.50');
    cy.get('[data-testid=product-search]').type('nothing-matches');
    cy.contains('No products match');
    cy.get('[data-testid=product-search]').clear().type('cold');
    cy.get('[data-testid=products-table]').should('contain.text', 'Cold Brew Concentrate');

    // TEN-1: the checklist ticks itself.
    cy.contains('a', 'Dashboard').click();
    cy.get('[data-testid=step-brand]').should('have.attr', 'data-done', 'true');
    cy.get('[data-testid=step-products]').should('have.attr', 'data-done', 'true');
  });

  it('[BRD-5] deletes a brand only after typing its exact name', () => {
    cy.signUp(newEmail('brand-delete'), 'Delete Journey Co');
    cy.visit('/brands/new');
    cy.get('[data-testid=brand-name]').type('Short Lived');
    cy.get('[data-testid=brand-industry]').type('Retail');
    cy.get('[data-testid=brand-save]').click();
    cy.location('pathname').should('eq', '/brand/profile');

    cy.contains('[role=tab]', 'Delete').click();
    cy.get('[data-testid=delete-brand]').should('be.disabled');
    cy.get('[data-testid=delete-confirm]').type('short lived');
    cy.get('[data-testid=delete-brand]').should('be.disabled');
    cy.get('[data-testid=delete-confirm]').clear().type('Short Lived');
    cy.get('[data-testid=delete-brand]').should('be.enabled').click();

    cy.location('pathname').should('eq', '/brands');
    cy.contains('No brand yet');
    cy.get('[data-testid=brand-switcher]').should('not.exist');
  });
});
