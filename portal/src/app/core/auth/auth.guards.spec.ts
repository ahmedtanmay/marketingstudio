import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, Router, RouterStateSnapshot, UrlTree, provideRouter } from '@angular/router';
import { authGuard, permissionGuard } from './auth.guards';
import { AuthStore } from './auth.store';

class FakeAuthStore {
  signedIn = false;
  granted = new Set<string>();
  isAuthenticated = () => this.signedIn;
  can = (permission: string) => this.granted.has(permission);
}

describe('route guards', () => {
  let store: FakeAuthStore;
  const route = {} as ActivatedRouteSnapshot;
  const state = (url: string) => ({ url }) as RouterStateSnapshot;

  beforeEach(() => {
    store = new FakeAuthStore();
    TestBed.configureTestingModule({ providers: [provideRouter([]), { provide: AuthStore, useValue: store }] });
  });

  const run = <T>(fn: () => T): T => TestBed.runInInjectionContext(fn);
  const serialize = (result: unknown) => TestBed.inject(Router).serializeUrl(result as UrlTree);

  it('sends signed-out users to sign-in and remembers where they were going', () => {
    const result = run(() => authGuard(route, state('/settings/users')));

    expect(serialize(result)).toBe('/login?returnUrl=%2Fsettings%2Fusers');
  });

  it('lets signed-in users through', () => {
    store.signedIn = true;

    expect(run(() => authGuard(route, state('/dashboard')))).toBe(true);
  });

  it('[AUTH-6] blocks pages the role has no permission for', () => {
    store.signedIn = true;

    expect(serialize(run(() => permissionGuard('users.manage')(route, state('/settings/users'))))).toBe('/forbidden');

    store.granted.add('users.manage');
    expect(run(() => permissionGuard('users.manage')(route, state('/settings/users')))).toBe(true);
  });
});
