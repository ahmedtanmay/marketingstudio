import { computed, inject } from '@angular/core';
import { patchState, signalStore, withComputed, withMethods, withState } from '@ngrx/signals';
import { firstValueFrom } from 'rxjs';
import { AuthResponse, MeResponse } from '../api/api.types';
import { AuthApi } from './auth-api';

type AuthStatus = 'unknown' | 'authenticated' | 'anonymous';

interface AuthState {
  status: AuthStatus;
  /** Kept in memory only — never in localStorage (XSS-safe). The refresh token is an HttpOnly cookie. */
  accessToken: string | null;
  accessTokenExpiresAt: number | null;
  me: MeResponse | null;
}

const initialState: AuthState = { status: 'unknown', accessToken: null, accessTokenExpiresAt: null, me: null };

/** Refresh this long before the access token expires. */
const REFRESH_LEAD_MS = 60_000;

/**
 * Session state for the portal (NgRx SignalStore). Restores the session from the refresh cookie on
 * start-up, refreshes the 15-minute access token before it expires, and shares a single in-flight
 * refresh between concurrent callers (the API rejects reuse of a rotated refresh token).
 */
export const AuthStore = signalStore(
  { providedIn: 'root' },
  withState(initialState),
  withComputed(({ status, me }) => ({
    isAuthenticated: computed(() => status() === 'authenticated'),
    user: computed(() => me()?.user ?? null),
    tenant: computed(() => me()?.tenant ?? null),
    subscription: computed(() => me()?.subscription ?? null),
    permissions: computed(() => new Set(me()?.permissions ?? [])),
  })),
  withMethods((store, api = inject(AuthApi)) => {
    let refreshInFlight: Promise<boolean> | null = null;
    let refreshTimer: ReturnType<typeof setTimeout> | undefined;

    const clear = (): void => {
      clearTimeout(refreshTimer);
      patchState(store, { status: 'anonymous', accessToken: null, accessTokenExpiresAt: null, me: null });
    };

    const scheduleRefresh = (expiresAt: number): void => {
      clearTimeout(refreshTimer);
      const delay = Math.max(expiresAt - Date.now() - REFRESH_LEAD_MS, 5_000);
      refreshTimer = setTimeout(() => void refresh(), delay);
    };

    const loadMe = async (): Promise<void> => {
      const me = await firstValueFrom(api.me());
      patchState(store, { me, status: 'authenticated' });
    };

    const startSession = async (auth: AuthResponse): Promise<void> => {
      const expiresAt = Date.parse(auth.accessTokenExpiresAt);
      patchState(store, { accessToken: auth.accessToken, accessTokenExpiresAt: expiresAt });
      scheduleRefresh(expiresAt);
      await loadMe();
    };

    /** Exchanges the refresh cookie for a new access token. Resolves false when the session is gone. */
    const refresh = (): Promise<boolean> => {
      refreshInFlight ??= (async () => {
        try {
          const auth = await firstValueFrom(api.refresh());
          const expiresAt = Date.parse(auth.accessTokenExpiresAt);
          patchState(store, { accessToken: auth.accessToken, accessTokenExpiresAt: expiresAt });
          scheduleRefresh(expiresAt);
          if (store.me() === null) {
            await loadMe();
          } else {
            patchState(store, { status: 'authenticated' });
          }
          return true;
        } catch {
          clear();
          return false;
        } finally {
          refreshInFlight = null;
        }
      })();
      return refreshInFlight;
    };

    return {
      /** Called once at start-up: signed in if the refresh cookie is still valid. */
      async restore(): Promise<void> {
        await refresh();
      },

      refresh,

      async login(email: string, password: string): Promise<void> {
        await startSession(await firstValueFrom(api.login(email, password)));
      },

      async signUp(body: { name: string; email: string; password: string; companyName: string }): Promise<void> {
        await firstValueFrom(api.register(body));
        // Registration answers the same whether or not the email exists, so sign in to find out.
        await startSession(await firstValueFrom(api.login(body.email, body.password)));
      },

      async acceptInvite(token: string, name: string, password: string): Promise<void> {
        await startSession(await firstValueFrom(api.acceptInvite(token, name, password)));
      },

      async logout(): Promise<void> {
        try {
          await firstValueFrom(api.logout());
        } finally {
          clear();
        }
      },

      reloadMe: loadMe,

      /** Session ended elsewhere (e.g. refresh failed in the interceptor). */
      clear,

      can(permission: string): boolean {
        return store.permissions().has(permission);
      },
    };
  }),
);

export type AuthStoreType = InstanceType<typeof AuthStore>;
