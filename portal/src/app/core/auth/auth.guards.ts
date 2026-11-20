import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthStore } from './auth.store';

/** Signed-in users only; others go to sign-in and come back afterwards. */
export const authGuard: CanActivateFn = (_route, state) => {
  const store = inject(AuthStore);
  return store.isAuthenticated()
    ? true
    : inject(Router).createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
};

/** Sign-in / sign-up pages: signed-in users go straight to the dashboard. */
export const guestGuard: CanActivateFn = () =>
  inject(AuthStore).isAuthenticated() ? inject(Router).createUrlTree(['/dashboard']) : true;

/** Pages that need a permission (RBAC Matrix.md). Hidden in the menu too; this covers typed URLs. */
export const permissionGuard =
  (permission: string): CanActivateFn =>
  () =>
    inject(AuthStore).can(permission) ? true : inject(Router).createUrlTree(['/forbidden']);
