import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { InviteListItem, Role, UserListItem } from '../../../core/api/api.types';

/** AUTH-5 / AUTH-6 endpoints (Tenant Admin). */
@Injectable({ providedIn: 'root' })
export class TeamApi {
  private readonly http = inject(HttpClient);

  listUsers(): Observable<UserListItem[]> {
    return this.http.get<UserListItem[]>('/api/v1/users');
  }

  changeRole(userId: string, role: Role): Observable<void> {
    return this.http.put<void>(`/api/v1/users/${encodeURIComponent(userId)}/role`, { role });
  }

  deactivate(userId: string): Observable<void> {
    return this.http.post<void>(`/api/v1/users/${encodeURIComponent(userId)}/deactivate`, {});
  }

  reactivate(userId: string): Observable<void> {
    return this.http.post<void>(`/api/v1/users/${encodeURIComponent(userId)}/reactivate`, {});
  }

  listInvites(): Observable<InviteListItem[]> {
    return this.http.get<InviteListItem[]>('/api/v1/invites');
  }

  invite(email: string, role: Role): Observable<InviteListItem> {
    return this.http.post<InviteListItem>('/api/v1/invites', { email, role });
  }

  resendInvite(inviteId: string): Observable<InviteListItem> {
    return this.http.post<InviteListItem>(`/api/v1/invites/${encodeURIComponent(inviteId)}/resend`, {});
  }

  revokeInvite(inviteId: string): Observable<void> {
    return this.http.delete<void>(`/api/v1/invites/${encodeURIComponent(inviteId)}`);
  }
}
