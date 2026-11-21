# Billing and Subscriptions Design

## Purpose
Defines plans, entitlements, usage metering (AI credits), payments, subscription lifecycle and enforcement for AI Marketing Studio. This document is the **single source of truth for plan limits** referenced by AI Integration Design.md, Knowledge Base Design.md and Social Integrations Design.md. Delivered in Phase 8, with a minimal entitlement service earlier (see Delivery Plan).

## Principles
- **The payment provider owns payment state; the platform owns entitlements.** Stripe (or the chosen provider) is the source of truth for subscription and invoice status. The platform derives what a tenant may do from that state.
- **Limits are data, not code.** Plans and their limits live in configuration/database so pricing changes need no deployment.
- **Credits are an append-only ledger.** Balances are computed from ledger entries, never edited in place.
- **Never lose customer data on downgrade or non-payment.** Excess resources become read-only; nothing is deleted automatically except after a cancelled account's retention period.
- **Check before spending.** Quotas are checked before an LLM call, upload or publish — not after.

---

## Plans and Entitlements

Prices are to be set by the business (see Open Decisions). Limits below are **proposed defaults**.

| Entitlement | Trial (14 days) | Starter | Growth | Enterprise |
|---|---|---|---|---|
| Price / month | Free | TBD | TBD | Custom |
| Brands | 1 | 1 | 5 | Custom |
| Users (seats) | 2 | 3 | 15 | Custom |
| AI credits / month | 500 | TBD | TBD | Custom |
| Agents available | Content, Campaign | Content, Campaign, Analytics | All | All |
| Social channel connections | 2 | 3 | 15 | Custom |
| Scheduled posts / month | 20 | 100 | 1,000 | Custom |
| Knowledge documents per brand | 10 | 25 | 200 | Custom |
| Knowledge storage | 100 MB | 500 MB | 5 GB | Custom |
| Max upload file size | 10 MB | 10 MB | 20 MB | Custom |
| WhatsApp Support Agent | No | No | Yes | Yes |
| WhatsApp auto-reply | No | No | Yes | Yes |
| Agent run history retention | 30 days | 90 days | 180 days | Custom |
| Support | Docs | Email | Priority email | Dedicated |
| Annual billing discount | — | Yes | Yes | Contract |

### Entitlement keys
Each limit has a stable key used by code, e.g. `brands.max`, `seats.max`, `credits.monthly`, `agents.allowed`, `channels.max`, `posts.monthlyScheduled`, `kb.docsPerBrand`, `kb.storageBytes`, `kb.maxFileBytes`, `whatsapp.supportAgent`, `whatsapp.autoReply`, `runs.retentionDays`. Enterprise tenants can have per-tenant overrides.

---

## AI Credits

### Definition
- **1 credit = a fixed amount of underlying AI cost** (e.g. USD 0.01 at cost — final value set with pricing). Credits abstract over tokens, so customer pricing stays stable when models or provider prices change.
- The Usage Meter (AI Integration Design.md) converts each LLM and embedding call's cost into credits: `credits = ceil(costUsd / creditValueUsd)`, summed per run.
- Margin is applied in the **price of credits/plans**, not in the conversion — so the ledger always reflects true cost.

### Typical consumption (to show in the UI; recalculated after model benchmark)
| Action | Approx. credits |
|---|---|
| Generate 1 social post | TBD |
| Plan a 4-week campaign | TBD |
| Support reply (WhatsApp) | TBD |
| Index a 20-page document | TBD |

### Credit sources and expiry
| Source | Expiry |
|---|---|
| Monthly plan allowance | Resets at each billing period; unused credits do not roll over |
| Purchased top-up packs | 12 months from purchase |
| Promotional / support grants (Super Admin) | Set per grant |

Consumption order: monthly allowance first, then the top-up that expires soonest.

### Ledger
Every change is a row in `creditLedger`: `grant` (period allowance, top-up, promo), `consume` (per agent run / ingestion job), `expire`, `adjust` (Super Admin, with reason). Balance = sum of entries for the current period + unexpired top-ups. The current balance is cached in Redis and rebuilt from the ledger if missing.

### Enforcement
1. Before an agent run: estimate credits (prompt size × tier price, conservative) and **reserve** them in Redis.
2. If available balance < estimate → reject with `QUOTA_EXCEEDED` (and suggest a top-up).
3. After the run: write the actual `consume` entry and release the reservation.
4. Notifications to Tenant Admin at **80%** and **100%** of monthly allowance.
5. Running out mid-run never cuts a run off; the small overage is recorded and deducted.

---

## Payment Provider

**Recommendation: Stripe Billing** (Checkout, Customer Portal, Subscriptions, Invoices, Tax). If the company primarily bills Indian customers in INR, evaluate **Razorpay Subscriptions** as an alternative or second provider (see Open Decisions). The platform talks to the provider through `IBillingProvider` so it can be swapped.

### What lives where
| Data | Owner |
|---|---|
| Card / payment method, invoices, tax calculation, subscription status | Stripe |
| Plan catalogue (limits), entitlements, credit ledger, usage | Platform (MongoDB) |
| Mapping tenant ↔ Stripe customer/subscription | Platform (`subscriptions`) |

No card data ever touches platform servers (Stripe Checkout / Elements), which keeps PCI scope to SAQ-A.

### Integration
- **Checkout Session** for new subscriptions and plan purchase.
- **Customer Portal** for payment method changes, invoices download, cancellation.
- **One-time Checkout** for credit top-up packs.
- Stripe Price IDs are stored on the plan record (`stripePriceIdMonthly`, `stripePriceIdAnnual`).

### Webhooks
Endpoint: `POST /api/v1/webhooks/stripe`
1. Verify the `Stripe-Signature` header with the webhook secret (Docker Secrets).
2. Deduplicate by event id (`billingEvents` collection, unique index).
3. Process in a Hangfire job; return 200 immediately.
4. Handle events out of order: always re-fetch the current subscription from Stripe instead of trusting event payload order.

| Event | Action |
|---|---|
| `checkout.session.completed` | Link Stripe customer/subscription to tenant; for top-ups, add `grant` ledger entry |
| `customer.subscription.created` / `updated` | Sync plan, status, period dates; recompute entitlements |
| `customer.subscription.deleted` | Status `cancelled`; start retention countdown |
| `invoice.paid` | Status `active`; on new period, grant monthly credits |
| `invoice.payment_failed` | Status `past_due`; notify Tenant Admin |
| `customer.subscription.trial_will_end` | Remind Tenant Admin 3 days before trial ends |

A nightly **reconciliation job** compares every tenant's subscription with Stripe and fixes drift (missed webhooks).

---

## Subscription Lifecycle

```text
signup -> trialing --(payment method + plan)--> active
trialing --(trial ends, no payment)--> expired (read-only)
active --(payment fails)--> past_due --(paid within grace)--> active
past_due --(grace period ends: 7 days)--> suspended (read-only)
suspended --(paid)--> active
active --(cancel)--> active until period end --> cancelled (read-only)
cancelled / expired --(90 days)--> scheduled for deletion (warning emails at 30, 7, 1 days)
```

### What each state allows
| State | Log in & view | Run agents | Publish / scheduled posts | Edit data | Billing pages |
|---|---|---|---|---|---|
| trialing / active | Yes | Yes | Yes | Yes | Yes |
| past_due (grace) | Yes | Yes | Yes | Yes | Yes + banner |
| suspended / expired / cancelled | Yes | No | **Paused** (not deleted) | No | Yes |

Scheduled posts are paused, not cancelled, so they resume after payment (posts whose time has passed become `failed` with reason "subscription inactive").

Tenant deletion after retention follows the erasure process in Knowledge Base Design.md and Social Integrations Design.md (vectors, SeaweedFS objects, tokens, conversations).

---

## Plan Changes
| Change | Behaviour |
|---|---|
| Upgrade | Immediate; prorated charge via Stripe; new limits and the **difference** in monthly credits granted immediately |
| Downgrade | At end of current period. Before confirming, the UI lists anything over the new limits |
| Over limit after downgrade | Excess brands/channels/users become **read-only/disabled** (tenant chooses which stay active); nothing is deleted |
| Monthly ↔ annual | Takes effect at period end |
| Seats | Included per plan; extra seats not sold in MVP (upgrade instead) |

## Trial and Signup
- Signup starts at the public website (`/signup?plan=growth`) and completes in the portal (Next.js → Angular handoff, see NextJs_Public_Website_Architecture.md).
- Signup creates **user + tenant + trial subscription** in one transaction; no card required for the trial.
- One trial per email domain / payment fingerprint to limit abuse; disposable email domains blocked.
- Trial agents are limited (see plan table) and trial content is watermarked in the portal preview only (not in published posts).

## Taxes and Invoices
- Stripe Tax calculates VAT/GST from the customer's billing address and tax ID.
- B2B customers can enter a tax ID (e.g. VAT, GSTIN) shown on invoices.
- Invoices are generated and emailed by Stripe; the portal lists them via the API.
- Prices displayed per currency configured on Stripe Prices (USD at launch; others per Open Decisions).

## Enterprise
- Contract-based: Super Admin creates a custom plan or per-tenant overrides; billed by invoice (Stripe invoicing, net 30).
- Custom credit allowance, retention and limits via entitlement overrides.

---

## Entitlement Service
`IEntitlementService` (Application layer) is the only place limits are checked.

```csharp
public interface IEntitlementService
{
    Task<Entitlements> GetAsync(string tenantId, CancellationToken ct);
    Task EnsureAllowedAsync(string tenantId, string entitlementKey, long requestedAmount, CancellationToken ct);
    Task<CreditReservation> ReserveCreditsAsync(string tenantId, int estimatedCredits, CancellationToken ct);
}
```

- Implemented as a **MediatR pipeline behavior** for commands marked with `[RequiresEntitlement("brands.max")]`, plus direct calls in the agent runtime, ingestion and publishing jobs.
- Entitlements are cached in Redis per tenant (TTL 5 min; invalidated on subscription change).
- Rejections return `402 Payment Required` with error code `PLAN_LIMIT_REACHED` or `QUOTA_EXCEEDED` and the limit that was hit, so the portal can show an upgrade prompt.

---

## Data Model
See MongoDB Collection Schemas.md: `plans`, `subscriptions`, `creditLedger`, `billingEvents`; `tenants.subscriptionPlan` is replaced by a reference to `subscriptions`.

## API Endpoints
```text
GET    /api/v1/billing/plans                    (public; also used by the website pricing page)
GET    /api/v1/billing/subscription
POST   /api/v1/billing/checkout                 (plan + interval -> Stripe Checkout URL)
POST   /api/v1/billing/portal                   (-> Stripe Customer Portal URL)
POST   /api/v1/billing/change-plan              (upgrade now / downgrade at period end)
GET    /api/v1/billing/downgrade-preview?plan=  (what would exceed the new limits)
POST   /api/v1/billing/credits/checkout         (top-up pack -> Checkout URL)
GET    /api/v1/billing/credits                  (balance, period usage, ledger page)
GET    /api/v1/billing/usage?from=&to=          (credits by agent/brand/day)
GET    /api/v1/billing/invoices
POST   /api/v1/webhooks/stripe
--- Super Admin ---
GET    /api/v1/admin/tenants/{id}/billing
POST   /api/v1/admin/tenants/{id}/credits       (adjust, with reason)
PUT    /api/v1/admin/tenants/{id}/entitlements  (enterprise overrides)
```

Permissions: billing pages and plan changes — Tenant Admin ("Manage" in RBAC Matrix.md); usage view — Tenant Admin and Marketing Manager; admin endpoints — Super Admin only.

## Portal UI (Billing module)
- Current plan, status, renewal date, trial days left.
- Credits: balance gauge, usage by agent and brand over time, top-up button.
- Limits overview: brands, seats, channels, documents vs plan limits.
- Plan comparison + upgrade/downgrade with downgrade preview.
- Invoices list; "Manage payment method" (Customer Portal).
- Banners: trial ending, past due, suspended, 80%/100% credits.

---

## Testing
- Unit: credit ledger math (allowance, top-up order, expiry, overage), lifecycle state transitions, entitlement checks, downgrade preview.
- Integration: Stripe webhook handling with signed test payloads; idempotency (same event twice); out-of-order events.
- **Stripe test mode + Test Clocks** to simulate trial end, renewals, failed payments and grace period end in minutes.
- Concurrency: parallel agent runs cannot overspend credits (reservation test).
- E2E (staging): signup → trial → checkout → upgrade → top-up → payment failure → suspended → paid → active.

## Observability and Business Metrics
- Metrics: MRR, active subscriptions by plan, trial → paid conversion, churn, past_due count, credits consumed vs granted, **AI cost vs revenue per tenant (gross margin)**.
- Alerts: webhook signature failures, reconciliation drift found, tenant with AI cost > revenue for the period.
- Audit log: all plan changes, credit adjustments and entitlement overrides (who, when, why).

---

## Delivery Plan
| When | Work |
|---|---|
| Phase 1 | Tenant + trial subscription created at signup (no payments) |
| Phase 5 | `IEntitlementService` with plans from configuration; credit ledger + reservations used by the agent runtime |
| Phase 8, week 1 | Stripe products/prices, Checkout, Customer Portal, webhooks, reconciliation job |
| Phase 8, week 2 | Lifecycle states & enforcement, upgrades/downgrades, top-ups, notifications |
| Phase 8, week 3 | Billing UI, Super Admin tools, taxes/invoices, Test Clock E2E tests, pricing page integration |

## Open Decisions
- **Prices** for Starter/Growth, credit allowance per plan, top-up pack sizes and price, credit value (USD per credit). Must be set using the benchmarked AI cost per action so gross margin is positive at full usage. Infrastructure Costing.md proposes 1 credit = $0.01 and gives estimated credits per action and minimum prices per allowance.
- **Payment provider and currencies:** Stripe only (USD), or Stripe + Razorpay (INR) if Indian customers are a primary market.
- **Overage model:** hard stop at 0 credits (proposed) vs automatic pay-as-you-go overage.
- **Extra seats** as a paid add-on vs upgrade-only.
- **Free plan** after trial (permanent limited tier) vs trial-only.
