# Multi-Tenant Design

## Strategy
**Shared infrastructure, shared database, shared collections**, with every tenant-owned record tagged by `tenantId` (and `brandId` where brand-scoped). Isolation is enforced in code at a small number of choke points, and proven by automated tests on every merge (Test Strategy.md).

### Why this model
| Benefit | Trade-off | Mitigation |
|---|---|---|
| Lowest cost; one deployment | A missing filter could leak data | Choke-point repositories, architecture tests, isolation suite |
| Simple operations and upgrades | Noisy neighbours share resources | Per-tenant rate limits, concurrency limits, quotas |
| Easy cross-tenant admin and analytics | Per-tenant restore is harder | Per-tenant export tooling; point-in-time restore to a scratch DB |

Enterprise tenants can later be moved to dedicated collections/servers behind the same interfaces (see Future Options).

## Hierarchy
```text
Platform (Super Admin)
 └── Tenant (company / workspace; billing unit)
      ├── Users (roles per tenant — RBAC Matrix.md)
      ├── Subscription, credits, entitlements
      └── Brands
           ├── Products, knowledge, media
           ├── Agents, campaigns, content
           └── Channel connections, conversations, analytics
```
A user belongs to exactly one tenant in MVP (multi-workspace membership is post-MVP).

---

## Tenant Resolution
1. User signs in → access token (JWT) includes `tenantId`, `userId`, roles.
2. `TenantContextMiddleware` reads the claims into a scoped `ITenantContext` (tenantId, userId, roles, active brandId from the `X-Brand-Id` header — validated to belong to the tenant).
3. Anything supplied as `tenantId` in a body, route or query string is **ignored**.
4. Requests without a tenant (public endpoints, webhooks) run with an explicit **system context**; their handlers resolve the tenant from trusted data (e.g. Meta phone number id → `channelConnections.tenantId`, Stripe customer id → `subscriptions.tenantId`).
5. Background jobs carry `tenantId`/`brandId` as job arguments and restore `ITenantContext` before running.

## Isolation by Store

| Store | Mechanism |
|---|---|
| **MongoDB** | `TenantRepository<T>` base class adds `tenantId == ctx.TenantId` to every filter and sets it on every insert; `IMongoCollection<T>` is only reachable inside Infrastructure repositories. Every collection's indexes start with `tenantId`. |
| **Qdrant** | One collection; `tenantId` payload index configured as tenant key; `QdrantKnowledgeStore` always adds `tenantId` + `brandId` `must` filters (Knowledge Base Design.md). |
| **Object storage (SeaweedFS)** | Object keys prefixed `{tenantId}/{brandId}/…`; keys are built from tenant-scoped records only. Storage is never exposed: files are served through API signed links (Data Protection, 1 h expiry) that name one asset. |
| **Redis** | Every key namespaced `t:{tenantId}:…`; helper builds keys — no raw string keys in feature code. |
| **Hangfire** | Jobs receive tenant/brand ids as arguments; job filter restores tenant context; dashboard restricted to Super Admin. |
| **SignalR** | Connections join groups `tenant:{tenantId}` and `brand:{brandId}` after server-side validation; messages are sent only to those groups. |
| **LLM calls** | One tenant's data per call; tools receive tenant/brand from run context, never from the model (AI Integration Design.md). |
| **Logs/metrics** | `tenantId` attached to log scope and traces; no tenant content in logs. |
| **Search indexes / analytics** | Same repository rules; Super Admin aggregates go through explicit admin queries. |

## Enforcement and Guard Rails
- **Architecture tests** (e.g. NetArchTest) fail the build if Application or Api code references `IMongoCollection`, the Qdrant client or the SeaweedFS client directly.
- **Route coverage test:** every non-public API route must have a cross-tenant test (Test Strategy.md → Tenant isolation).
- **Unique indexes** that must be unique per tenant include `tenantId` (e.g. `{tenantId, name}` on brands).
- **Cross-tenant access** exists only for Super Admin through a separate `IPlatformAdminRepository` used by `/api/v1/admin/*` endpoints, always audit-logged.
- **Code review checklist:** new collection → tenant field, index, repository; new cache key → helper; new job → tenant arguments.

## Noisy-Neighbour Protection
| Resource | Control |
|---|---|
| API requests | Rate limits per user and per tenant (Redis) |
| AI usage | Credits/quotas + per-tenant concurrency limit on LLM calls |
| Ingestion | Per-tenant job queue concurrency (max 2 documents in parallel) |
| Publishing | Per-connection token bucket honouring Meta usage headers |
| Storage | Entitlement limits on documents, storage, channels |

## Tenant Lifecycle
| Event | Actions |
|---|---|
| Create (signup) | Tenant + Tenant Admin user + trial subscription in one transaction; default settings |
| Suspend (non-payment / abuse) | Read-only enforcement; scheduled posts paused; agents disabled (Billing and Subscriptions Design.md) |
| Export (GDPR / offboarding) | Background job builds a ZIP of tenant data (JSON per collection + SeaweedFS files) available via a time-limited link |
| Delete (after retention or erasure request) | Delete MongoDB documents by `tenantId`, Qdrant points by `tenantId`, SeaweedFS prefix `{tenantId}/`, Redis keys `t:{tenantId}:*`, revoke channel tokens and unsubscribe webhooks, cancel Stripe customer; record a deletion certificate in the platform audit log |

Backups age out under the normal retention (30 days); restores after deletion must re-apply the deletion list.

## Tenant Theming
Tenant branding (logo, colours) from `tenants` settings is loaded at sign-in and applied via CSS variables in the portal (Angular_Enterprise_Architecture.md).

## Future Options (Enterprise)
- Dedicated Qdrant collection or MongoDB database per large tenant, selected by a tenant-routing table behind the same repository interfaces.
- Dedicated deployment (single-tenant) for customers with residency or compliance needs.
- Customer-managed encryption keys — post-MVP.
