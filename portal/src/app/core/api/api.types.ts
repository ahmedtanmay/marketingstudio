// Shapes returned by the ASP.NET Core API (camelCase JSON). Keep in sync with docs/OpenAPI Specification.md.

export type Role = 'TenantAdmin' | 'MarketingManager' | 'ContentManager' | 'Viewer' | 'SuperAdmin';

export const TENANT_ROLES: readonly Role[] = ['TenantAdmin', 'MarketingManager', 'ContentManager', 'Viewer'];

export const ROLE_LABELS: Record<Role, string> = {
  TenantAdmin: 'Tenant Admin',
  MarketingManager: 'Marketing Manager',
  ContentManager: 'Content Manager',
  Viewer: 'Viewer',
  SuperAdmin: 'Super Admin',
};

export interface UserSummary {
  id: string;
  tenantId: string;
  email: string;
  displayName: string;
  roles: Role[];
  emailVerified: boolean;
}

export interface AuthResponse {
  accessToken: string;
  accessTokenExpiresAt: string;
  user: UserSummary;
}

export interface MeResponse {
  user: UserSummary;
  tenant: { id: string; name: string; status: string };
  subscription: { planId: string; status: string; trialEndsAt: string | null; trialDaysRemaining: number } | null;
  permissions: string[];
}

export interface UserListItem {
  id: string;
  email: string;
  displayName: string;
  role: Role;
  status: 'Active' | 'Deactivated';
  emailVerified: boolean;
  lastLoginAt: string | null;
  createdAt: string;
}

export interface InviteListItem {
  id: string;
  email: string;
  role: Role;
  expiresAt: string;
  lastSentAt: string;
}

export interface InviteDetails {
  workspaceName: string;
  email: string;
  role: Role;
}

export interface MessageResponse {
  message: string;
}

/** Permission names from the API (Domain/Users/Permissions.cs). */
export const Permission = {
  UsersManage: 'users.manage',
  BillingManage: 'billing.manage',
  ChannelsManage: 'channels.manage',
  ContentApprove: 'content.approve',
} as const;
