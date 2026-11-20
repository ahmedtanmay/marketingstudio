import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AuthResponse, MeResponse, UserSummary } from '../api/api.types';
import { AuthStore } from './auth.store';

const user: UserSummary = {
  id: 'u1',
  tenantId: 't1',
  email: 'ana@example.com',
  displayName: 'Ana',
  roles: ['TenantAdmin'],
  emailVerified: false,
};

const auth = (token: string): AuthResponse => ({
  accessToken: token,
  accessTokenExpiresAt: new Date(Date.now() + 15 * 60_000).toISOString(),
  user,
});

const me: MeResponse = {
  user,
  tenant: { id: 't1', name: 'Acme', status: 'Active' },
  subscription: { planId: 'trial', status: 'Trialing', trialEndsAt: null, trialDaysRemaining: 14 },
  permissions: ['users.manage', 'brands.read'],
};

/** Lets pending promise continuations run so the next HTTP call is issued. */
const tick = () => new Promise<void>((resolve) => setTimeout(resolve));

describe('AuthStore', () => {
  let store: InstanceType<typeof AuthStore>;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    store = TestBed.inject(AuthStore);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    store.clear();
    http.verify();
  });

  it('[AUTH-2] signs in, keeps the token in memory only and loads permissions', async () => {
    const login = store.login('ana@example.com', 'violet-harbour-lantern');
    http.expectOne({ method: 'POST', url: '/api/v1/auth/login' }).flush(auth('access-1'));
    await tick();
    http.expectOne('/api/v1/me').flush(me);
    await login;

    expect(store.isAuthenticated()).toBe(true);
    expect(store.accessToken()).toBe('access-1');
    expect(store.can('users.manage')).toBe(true);
    expect(store.can('billing.manage')).toBe(false);
    expect(localStorage.length).toBe(0);
    expect(sessionStorage.length).toBe(0);
  });

  it('[AUTH-3] restore() ends anonymous when the refresh cookie is gone', async () => {
    const restore = store.restore();
    http.expectOne({ method: 'POST', url: '/api/v1/auth/refresh' }).flush(null, { status: 401, statusText: 'Unauthorized' });
    await restore;

    expect(store.status()).toBe('anonymous');
    expect(store.accessToken()).toBeNull();
  });

  it('[AUTH-3] concurrent refreshes share one request (a rotated token must never be replayed)', async () => {
    const first = store.refresh();
    const second = store.refresh();
    http.expectOne('/api/v1/auth/refresh').flush(auth('access-2'));
    await tick();
    http.expectOne('/api/v1/me').flush(me);

    expect(await first).toBe(true);
    expect(await second).toBe(true);
    expect(store.accessToken()).toBe('access-2');
  });

  it('[AUTH-3] sign out clears the session even if the server call fails', async () => {
    const logout = store.logout();
    http.expectOne('/api/v1/auth/logout').flush(null, { status: 500, statusText: 'Error' });
    await expect(logout).rejects.toBeTruthy();

    expect(store.status()).toBe('anonymous');
    expect(store.me()).toBeNull();
  });
});
