import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { AuthResponse, InviteDetails, MeResponse, MessageResponse } from '../api/api.types';

export const AUTH_BASE = '/api/v1/auth';

/** Thin HTTP client for the auth endpoints. The refresh token travels only as an HttpOnly cookie. */
@Injectable({ providedIn: 'root' })
export class AuthApi {
  private readonly http = inject(HttpClient);

  register(body: { name: string; email: string; password: string; companyName: string }): Observable<MessageResponse> {
    return this.http.post<MessageResponse>(`${AUTH_BASE}/register`, body);
  }

  login(email: string, password: string): Observable<AuthResponse> {
    return this.http.post<AuthResponse>(`${AUTH_BASE}/login`, { email, password });
  }

  refresh(): Observable<AuthResponse> {
    return this.http.post<AuthResponse>(`${AUTH_BASE}/refresh`, {});
  }

  logout(): Observable<void> {
    return this.http.post<void>(`${AUTH_BASE}/logout`, {});
  }

  forgotPassword(email: string): Observable<MessageResponse> {
    return this.http.post<MessageResponse>(`${AUTH_BASE}/forgot-password`, { email });
  }

  resetPassword(email: string, token: string, newPassword: string): Observable<void> {
    return this.http.post<void>(`${AUTH_BASE}/reset-password`, { email, token, newPassword });
  }

  verifyEmail(userId: string, token: string): Observable<void> {
    return this.http.post<void>(`${AUTH_BASE}/verify-email`, { userId, token });
  }

  resendVerification(): Observable<void> {
    return this.http.post<void>(`${AUTH_BASE}/resend-verification`, {});
  }

  me(): Observable<MeResponse> {
    return this.http.get<MeResponse>('/api/v1/me');
  }

  lookupInvite(token: string): Observable<InviteDetails> {
    return this.http.get<InviteDetails>('/api/v1/invites/lookup', { params: { token } });
  }

  acceptInvite(token: string, name: string, password: string): Observable<AuthResponse> {
    return this.http.post<AuthResponse>('/api/v1/invites/accept', { token, name, password });
  }
}
