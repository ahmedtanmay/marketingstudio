import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, from, switchMap, throwError } from 'rxjs';
import { AUTH_BASE } from './auth-api';
import { AuthStore } from './auth.store';

/** Auth endpoints manage their own credentials (cookie / body) and must never trigger a refresh loop. */
const isAuthEndpoint = (url: string): boolean => url.startsWith(`${AUTH_BASE}/`) && !url.endsWith('/resend-verification');

const isApi = (url: string): boolean => url.startsWith('/api/');

/**
 * Adds the bearer token to API calls. On 401 it refreshes once (shared with other requests) and
 * retries; if the session is gone it sends the user to sign in, keeping where they were.
 */
export const authInterceptor: HttpInterceptorFn = (request, next) => {
  if (!isApi(request.url) || isAuthEndpoint(request.url)) {
    return next(request);
  }

  const store = inject(AuthStore);
  const router = inject(Router);
  const withToken = (token: string | null) =>
    token ? request.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : request;

  return next(withToken(store.accessToken())).pipe(
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse) || error.status !== 401 || store.accessToken() === null) {
        return throwError(() => error);
      }

      return from(store.refresh()).pipe(
        switchMap((refreshed) => {
          if (!refreshed) {
            void router.navigate(['/login'], { queryParams: { returnUrl: router.url } });
            return throwError(() => error);
          }
          return next(withToken(store.accessToken()));
        }),
      );
    }),
  );
};
