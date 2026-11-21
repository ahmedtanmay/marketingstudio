import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { BrandSummary } from '../api/api.types';
import { AuthStore } from '../auth/auth.store';
import { BrandStore } from './brand.store';

const brands: BrandSummary[] = [
  { id: 'b1', name: 'Acme Coffee', industry: 'Food & Beverage' },
  { id: 'b2', name: 'Acme Tea', industry: 'Food & Beverage' },
];

class FakeAuthStore {
  user = () => ({ id: 'user-1' });
}

describe('BrandStore', () => {
  let store: InstanceType<typeof BrandStore>;
  let http: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: AuthStore, useValue: new FakeAuthStore() }],
    });
    store = TestBed.inject(BrandStore);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    localStorage.clear();
    vi.restoreAllMocks();
  });

  const load = async (list = brands) => {
    const loading = store.load();
    http.expectOne('/api/v1/brands').flush(list);
    await loading;
  };

  it('[BRD-4] picks the first brand when nothing was remembered', async () => {
    await load();

    expect(store.activeBrandId()).toBe('b1');
    expect(store.activeBrand()?.name).toBe('Acme Coffee');
  });

  it('[BRD-4] remembers the chosen brand per user', async () => {
    await load();
    store.setActive('b2');

    expect(localStorage.getItem('ms.activeBrand.user-1')).toBe('b2');
  });

  it('[BRD-4] restores the remembered brand, and ignores it if the brand is gone', async () => {
    localStorage.setItem('ms.activeBrand.user-1', 'b2');
    await load();
    expect(store.activeBrandId()).toBe('b2');

    store.reset();
    localStorage.setItem('ms.activeBrand.user-1', 'deleted-brand');
    await load();
    expect(store.activeBrandId()).toBe('b1');
  });

  it('ignores ids that are not one of the workspace brands', async () => {
    await load();
    store.setActive('someone-elses-brand');

    expect(store.activeBrandId()).toBe('b1');
  });

  it('still works when browser storage is blocked', async () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('blocked');
    });

    await load();
    store.setActive('b2');

    expect(store.activeBrandId()).toBe('b2');
  });

  it('has no active brand when the workspace has none', async () => {
    await load([]);

    expect(store.activeBrandId()).toBeNull();
    expect(store.hasBrands()).toBe(false);
  });
});
