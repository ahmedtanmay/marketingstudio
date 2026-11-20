import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { authInterceptor } from './auth.interceptor';
import { AuthStore } from './auth.store';

/** Minimal stand-in for the store: the interceptor only needs the token and refresh(). */
class FakeAuthStore {
  token: string | null = null;
  refreshResult = true;
  refreshCalls = 0;
  accessToken = () => this.token;
  refresh = async () => {
    this.refreshCalls++;
    if (this.refreshResult) {
      this.token = 'new-token';
    }
    return this.refreshResult;
  };
}

describe('authInterceptor', () => {
  let http: HttpClient;
  let backend: HttpTestingController;
  let store: FakeAuthStore;

  beforeEach(() => {
    store = new FakeAuthStore();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([{ path: 'login', children: [] }]),
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        { provide: AuthStore, useValue: store },
      ],
    });
    http = TestBed.inject(HttpClient);
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => backend.verify());

  it('adds the bearer token to API calls', async () => {
    store.token = 'token-1';
    const call = firstValueFrom(http.get('/api/v1/users'));
    const request = backend.expectOne('/api/v1/users');
    request.flush([]);
    await call;

    expect(request.request.headers.get('Authorization')).toBe('Bearer token-1');
  });

  it('never sends the bearer token to auth endpoints or other origins', async () => {
    store.token = 'token-1';
    const login = firstValueFrom(http.post('/api/v1/auth/login', {}));
    const other = firstValueFrom(http.get('https://example.com/x'));
    const loginRequest = backend.expectOne('/api/v1/auth/login');
    const otherRequest = backend.expectOne('https://example.com/x');
    loginRequest.flush({});
    otherRequest.flush({});
    await Promise.all([login, other]);

    expect(loginRequest.request.headers.has('Authorization')).toBe(false);
    expect(otherRequest.request.headers.has('Authorization')).toBe(false);
  });

  it('[AUTH-3] on 401 refreshes once and retries with the new token', async () => {
    store.token = 'old-token';
    const call = firstValueFrom(http.get('/api/v1/me'));
    backend.expectOne('/api/v1/me').flush(null, { status: 401, statusText: 'Unauthorized' });
    await new Promise((resolve) => setTimeout(resolve));
    const retry = backend.expectOne('/api/v1/me');
    retry.flush({ ok: true });

    expect(await call).toEqual({ ok: true });
    expect(store.refreshCalls).toBe(1);
    expect(retry.request.headers.get('Authorization')).toBe('Bearer new-token');
  });

  it('[AUTH-3] gives up when the session cannot be refreshed', async () => {
    store.token = 'old-token';
    store.refreshResult = false;
    const call = firstValueFrom(http.get('/api/v1/me'));
    backend.expectOne('/api/v1/me').flush(null, { status: 401, statusText: 'Unauthorized' });

    await expect(call).rejects.toMatchObject({ status: 401 });
    expect(store.refreshCalls).toBe(1);
  });
});
