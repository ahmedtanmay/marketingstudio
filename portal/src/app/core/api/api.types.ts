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
  BrandsRead: 'brands.read',
  BrandsWrite: 'brands.write',
  BrandsDelete: 'brands.delete',
  BrandContentWrite: 'brands.content.write',
  ProductsRead: 'products.read',
  ProductsWrite: 'products.write',
  BillingManage: 'billing.manage',
  ChannelsManage: 'channels.manage',
  ContentApprove: 'content.approve',
} as const;

// ---- Sprint 3: brands, products, media, onboarding ----

export const TONE_PRESETS = ['Friendly', 'Professional', 'Playful', 'Custom'] as const;
export type TonePreset = (typeof TONE_PRESETS)[number];

export interface BrandVoice {
  tonePreset: TonePreset;
  guidelines: string;
  examplePosts: string[];
  bannedTerms: string[];
}

export interface BrandSummary {
  id: string;
  name: string;
  industry: string;
}

export interface BrandProfileInput {
  name: string;
  industry: string;
  languages: string[];
  timezone: string;
  description: string | null;
  websiteUrl: string | null;
  targetAudience: string | null;
}

export interface Brand extends BrandProfileInput {
  id: string;
  voice: BrandVoice;
  createdAt: string;
  updatedAt: string;
}

export interface ProductInput {
  name: string;
  description: string | null;
  category: string | null;
  price: number | null;
  currency: string | null;
  features: string[];
  url: string | null;
  imageAssetIds: string[];
}

export interface Product extends ProductInput {
  id: string;
  brandId: string;
  createdAt: string;
  updatedAt: string;
}

export interface PagedResult<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}

export interface MediaAsset {
  id: string;
  brandId: string;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  tags: string[];
  /** Signed link, valid for about an hour. */
  url: string;
  createdAt: string;
}

export interface OnboardingStatus {
  workspaceCreated: boolean;
  emailVerified: boolean;
  brandCreated: boolean;
  productAdded: boolean;
  teamInvited: boolean;
  knowledgeUploaded: boolean;
  channelConnected: boolean;
  firstPostGenerated: boolean;
}

export const LANGUAGES: readonly { code: string; name: string }[] = [
  { code: 'en', name: 'English' }, { code: 'hi', name: 'Hindi' }, { code: 'bn', name: 'Bengali' },
  { code: 'ta', name: 'Tamil' }, { code: 'te', name: 'Telugu' }, { code: 'mr', name: 'Marathi' },
  { code: 'gu', name: 'Gujarati' }, { code: 'kn', name: 'Kannada' }, { code: 'ml', name: 'Malayalam' },
  { code: 'pa', name: 'Punjabi' }, { code: 'ur', name: 'Urdu' }, { code: 'ar', name: 'Arabic' },
  { code: 'es', name: 'Spanish' }, { code: 'fr', name: 'French' }, { code: 'de', name: 'German' },
  { code: 'pt', name: 'Portuguese' }, { code: 'ja', name: 'Japanese' }, { code: 'zh', name: 'Chinese' },
];

export const INDUSTRIES: readonly string[] = [
  'Food & Beverage', 'Retail', 'Fashion & Apparel', 'Beauty & Personal Care', 'Health & Wellness', 'Education',
  'Travel & Hospitality', 'Real Estate', 'Technology', 'Finance', 'Professional Services', 'Home & Living', 'Other',
];

export const CURRENCIES: readonly string[] = ['INR', 'USD', 'EUR', 'GBP', 'AED', 'SGD', 'AUD'];
