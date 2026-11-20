import { newEmail } from '../support/e2e';

describe('Team: invites and roles', () => {
  it('[AUTH-5] admin invites a Content Manager who joins with limited access', () => {
    const admin = newEmail('admin');
    const member = newEmail('member');
    cy.signUp(admin, 'Invite Roasters');

    cy.contains('a', 'Team').click();
    cy.location('pathname').should('eq', '/settings/users');
    cy.get('[data-testid=members-table]').should('contain.text', admin);
    cy.checkPageA11y();

    cy.get('[data-testid=invite-button]').click();
    cy.get('[data-testid=invite-email]').type(member);
    cy.get('[data-testid=invite-role]').click();
    cy.contains('mat-option', 'Content Manager').click();
    cy.checkPageA11y();
    cy.get('[data-testid=invite-send]').click();
    cy.contains('Invite sent');
    cy.contains('[role=tab]', 'Pending invites (1)').click();
    cy.get('[data-testid=invites-table]').should('contain.text', member);

    // The invitee opens the emailed link in a fresh session.
    cy.get('[data-testid=user-menu]').click();
    cy.get('[data-testid=sign-out]').click();
    cy.emailLink(member, 'invited').then((path) => cy.visit(path));
    cy.contains('Invite Roasters');
    cy.contains('Content Manager');
    cy.checkPageA11y();
    cy.get('[data-testid=name]').type('Cora Content');
    cy.get('[data-testid=password]').type('copper-meadow-whistle', { log: false });
    cy.get('[data-testid=submit]').click();

    cy.location('pathname').should('eq', '/dashboard');
    cy.get('[data-testid=workspace-name]').should('have.text', 'Invite Roasters');
    cy.get('[data-testid=verify-banner]').should('not.exist');
    cy.contains('a', 'Team').should('not.exist');

    cy.visit('/settings/users');
    cy.location('pathname').should('eq', '/forbidden');
  });

  it('[AUTH-6] admin changes a role and deactivates a member', () => {
    const admin = newEmail('admin2');
    const member = newEmail('viewer');
    cy.signUp(admin, 'Role Roasters');

    cy.visit('/settings/users');
    cy.get('[data-testid=invite-button]').click();
    cy.get('[data-testid=invite-email]').type(member);
    cy.get('[data-testid=invite-role]').click();
    cy.contains('mat-option', 'Viewer').click();
    cy.get('[data-testid=invite-send]').click();
    cy.contains('Invite sent');

    cy.get('[data-testid=user-menu]').click();
    cy.get('[data-testid=sign-out]').click();
    cy.emailLink(member, 'invited').then((path) => cy.visit(path));
    cy.get('[data-testid=name]').type('Val Viewer');
    cy.get('[data-testid=password]').type('copper-meadow-whistle', { log: false });
    cy.get('[data-testid=submit]').click();
    cy.location('pathname').should('eq', '/dashboard');
    cy.get('[data-testid=user-menu]').click();
    cy.get('[data-testid=sign-out]').click();

    cy.signIn(admin);
    cy.location('pathname').should('eq', '/dashboard');
    cy.visit('/settings/users');
    cy.get('[aria-label="Role for Val Viewer"]').click();
    cy.contains('mat-option', 'Marketing Manager').click();
    cy.contains('Val Viewer is now Marketing Manager');

    cy.get('[aria-label="Actions for Val Viewer"]').click();
    cy.contains('button', 'Deactivate').click();
    cy.contains('Val Viewer has been deactivated');
    cy.get('[data-testid=members-table]').contains('tr', 'Val Viewer').should('contain.text', 'Deactivated');
  });
});
