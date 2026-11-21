import { computed, inject } from '@angular/core';
import { patchState, signalStore, withComputed, withMethods, withState } from '@ngrx/signals';
import { firstValueFrom } from 'rxjs';
import { BrandSummary } from '../api/api.types';
import { AuthStore } from '../auth/auth.store';
import { BrandApi } from './brand-api';

interface BrandState {
  brands: BrandSummary[];
  activeBrandId: string | null;
  loaded: boolean;
}

/** Remembering the last brand is a per-browser convenience; it must never be required. */
const storageKey = (userId: string) => `ms.activeBrand.${userId}`;

function readRemembered(userId: string | undefined): string | null {
  if (!userId) {
    return null;
  }
  try {
    return localStorage.getItem(storageKey(userId));
  } catch {
    return null;
  }
}

function remember(userId: string | undefined, brandId: string | null): void {
  if (!userId) {
    return;
  }
  try {
    if (brandId) {
      localStorage.setItem(storageKey(userId), brandId);
    } else {
      localStorage.removeItem(storageKey(userId));
    }
  } catch {
    // Storage blocked (private mode, policy): the switcher still works for this session.
  }
}

/**
 * BRD-4: the brands of the workspace and the active brand shown in the header. Brand pages read
 * `activeBrandId` and reload when it changes; every request still names the brand explicitly and the
 * API checks it belongs to the caller's tenant.
 */
export const BrandStore = signalStore(
  { providedIn: 'root' },
  withState<BrandState>({ brands: [], activeBrandId: null, loaded: false }),
  withComputed(({ brands, activeBrandId }) => ({
    activeBrand: computed(() => brands().find((b) => b.id === activeBrandId()) ?? null),
    hasBrands: computed(() => brands().length > 0),
  })),
  withMethods((store, api = inject(BrandApi), auth = inject(AuthStore)) => {
    const choose = (brands: BrandSummary[], preferred: string | null): string | null =>
      brands.some((b) => b.id === preferred) ? preferred : (brands[0]?.id ?? null);

    return {
      async load(): Promise<void> {
        const brands = await firstValueFrom(api.listBrands());
        const active = choose(brands, store.activeBrandId() ?? readRemembered(auth.user()?.id));
        patchState(store, { brands, activeBrandId: active, loaded: true });
        remember(auth.user()?.id, active);
      },

      setActive(brandId: string): void {
        if (store.brands().some((b) => b.id === brandId)) {
          patchState(store, { activeBrandId: brandId });
          remember(auth.user()?.id, brandId);
        }
      },

      /** After create/rename: refresh the list and optionally switch to the brand. */
      async refresh(selectBrandId?: string): Promise<void> {
        const brands = await firstValueFrom(api.listBrands());
        const active = choose(brands, selectBrandId ?? store.activeBrandId());
        patchState(store, { brands, activeBrandId: active, loaded: true });
        remember(auth.user()?.id, active);
      },

      reset(): void {
        patchState(store, { brands: [], activeBrandId: null, loaded: false });
      },
    };
  }),
);
