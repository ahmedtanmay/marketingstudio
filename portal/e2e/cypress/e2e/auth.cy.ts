import { PASSWORD, newEmail } from '../support/e2e';

describe('Sign up, sign in and sign out', () => {
  it('[AUTH-1] signs up into a new workspace with a 14-day trial', () => {
    const email = newEmail('owner');
    cy.signUp(email, 'Bean There Ltd');

    cy.get('[data-testid=workspace-name]').should('have.text', 'Bean There Ltd');
    cy.get('[data-testid=trial-banner]').should('contain.text', '14 days left');
    cy.get('[data-testid=verify-banner]').should('be.visible');
    cy.contains('h1', 'Welcome, Ana');
    cy.checkPageA11y();
  });

  it('[AUTH-1] confirms the email from the verification link', () => {
    const email = newEmail('verify');
    cy.signUp(email);

    cy.emailLink(email, 'Verify your email').then((path) => cy.visit(path));
    cy.contains('your email is confirmed');
    cy.contains('a', 'Continue').click();
    cy.get('[data-testid=verify-banner]').should('not.exist');
  });

  it('[AUTH-2] [AUTH-3] stays signed in across reloads and signs out', () => {
    const email = newEmail('session');
    cy.signUp(email);

    cy.reload();
    cy.location('pathname').should('eq', '/dashboard');

    cy.get('[data-testid=user-menu]').click();
    cy.get('[data-testid=sign-out]').click();
    cy.location('pathname').should('eq', '/login');

    cy.visit('/dashboard');
    cy.location('pathname').should('eq', '/login');
    cy.location('search').should('contain', 'returnUrl=%2Fdashboard');
  });

  it('[AUTH-2] shows one generic error for a wrong password and returns to the requested page after sign-in', () => {
    const email = newEmail('return');
    cy.signUp(email);
    cy.get('[data-testid=user-menu]').click();
    cy.get('[data-testid=sign-out]').click();

    cy.signIn(email, 'not-the-password');
    cy.get('[data-testid=login-error]').should('contain.text', 'Invalid email or password');
    cy.checkPageA11y();

    cy.visit('/settings/users');
    cy.get('[data-testid=email]').type(email);
    cy.get('[data-testid=password]').type(PASSWORD, { log: false });
    cy.get('[data-testid=submit]').click();
    cy.location('pathname').should('eq', '/settings/users');
  });

  it('signed-out pages are accessible', () => {
    cy.visit('/signup');
    cy.checkPageA11y();
    cy.visit('/forgot-password');
    cy.checkPageA11y();
  });
});
