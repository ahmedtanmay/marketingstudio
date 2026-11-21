# Infrastructure Costing

## Purpose
Estimates the monthly running cost of AI Marketing Studio (infrastructure, AI usage, third-party services), the AI cost per user action, and the cost per tenant, so plan prices and credit allowances (Billing and Subscriptions Design.md) can be set with a positive margin.

> **Price basis:** researched November 2026. Several figures come from third-party pricing summaries that disagree with each other, and providers changed prices during 2026 (Hetzner raised dedicated-server prices by over 100% in June 2026). Every row has a confidence mark. **Re-check rows marked "verify" on the provider's own pricing page before committing a budget.**
>
> Confidence: ✅ consistent across sources · ⚠️ verify (sources disagree or estimate)
>
> Excluded: development team salaries, office, sales and marketing spend.

---

## 1. Server Sizing

The original MVP spec (4 vCPU, 8 GB RAM, 150 GB SSD) is **too small** once every service in the design documents runs on one server.

### Memory budget (single server, MVP)
| Service | RAM (typical) |
|---|---|
| MongoDB (WiredTiger cache + overhead) | 4–6 GB |
| Qdrant (≈ 250k vectors, scalar quantization, original vectors on disk) | 1–2 GB |
| Redis | 0.5–1 GB |
| ASP.NET Core API + Hangfire workers | 1.5–2 GB |
| Next.js website (SSR) | 0.5–1 GB |
| SeaweedFS | 0.5–1 GB |
| ClamAV (virus signatures loaded in memory) | 1.5–2 GB |
| Nginx (+ Angular static files) | < 0.2 GB |
| Prometheus + Grafana + Loki | 1.5–2 GB |
| OS + headroom for spikes | 2 GB |
| **Total** | **≈ 14–20 GB** |

### Vector storage check
200 tenants × ~30 documents × ~40 chunks ≈ 240k vectors × 1,536 dimensions × 4 bytes ≈ **1.5 GB** raw. Scalar quantization cuts in-memory size about 4×; originals stay on disk. At 1,000 brands with full document limits this grows to tens of GB — the trigger for moving Qdrant to its own server (Scaling Plan).

### Recommended MVP spec
| Environment | Spec | Notes |
|---|---|---|
| **Production** | **8 dedicated vCPU, 32 GB RAM, 240 GB NVMe** + 500 GB attached volume for SeaweedFS media | Dedicated vCPU avoids noisy-neighbour slowdowns for MongoDB and Qdrant |
| **Staging** | 4 dedicated vCPU, 16 GB RAM | Same images and config; ClamAV and monitoring can be slimmer |

---

## 2. Fixed Monthly Costs (MVP)

Example provider: Hetzner Cloud (EU). Equivalent specs from other providers are typically more expensive; given Hetzner's 2026 price changes, compare at purchase time.

| Item | Choice | Monthly | Confidence |
|---|---|---|---|
| Production server | Hetzner CCX33 (8 dedicated vCPU, 32 GB, 240 GB) | €138.49 | ✅ (post-June 2026 price) |
| Staging server | Hetzner CCX23 (4 dedicated vCPU, 16 GB) | €85.99 | ✅ |
| Server snapshots/backups | Provider backup option for production (~20% of server price) | ~€28 | ⚠️ verify |
| Media volume | 500 GB block volume for SeaweedFS | ~€25–30 | ⚠️ verify |
| Off-site backups | Object storage / storage box in a second location (MongoDB dumps, SeaweedFS, Qdrant snapshots) | ~€10 | ⚠️ verify |
| Transactional email | Postmark Basic (10k emails) or Amazon SES ($0.10 per 1,000) | $15 (Postmark) / ~$1 (SES) | ✅ |
| Domain | marketingstudio.ai (.ai domains are priced higher than .com) | ~$7 (≈ $80/yr) | ⚠️ verify |
| TLS certificates | Let's Encrypt | $0 | ✅ |
| Monitoring | Self-hosted Prometheus/Grafana/Loki (on the server) | $0 | ✅ |
| Source control + CI | GitHub Team, 5 users + Actions minutes | ~$20–40 | ⚠️ verify |
| AI for staging/evaluation | Capped API key for nightly eval and red-team suites | $100 (cap) | Budget choice |
| Error tracking (optional) | Hosted Sentry or self-hosted GlitchTip | $0–30 | ⚠️ verify |
| **Total** | | **≈ €290 + ≈ $165 ≈ $480 / month** | assumes €1 ≈ $1.10 ⚠️ |

Saving options: switch staging off outside working hours or use a shared-vCPU server for staging (saves ~€40–60); use SES instead of Postmark (saves ~$14).

---

## 3. AI Cost per Action

### Model price assumptions (per 1M tokens)
| Tier (AI Integration Design.md) | Example OpenAI model | Input | Output | Confidence |
|---|---|---|---|---|
| Fast | GPT-6 Luna | $0.10 | $0.50 | ✅ |
| Standard | GPT-6.1/6.2 Sol | $2 | $10 | ✅ |
| Reasoning | GPT-6.x Astra | $8–10 | $40–50 | ⚠️ verify (sources disagree) |
| Embedding | text-embedding-3-small | ~$0.02 | — | ⚠️ verify |

Cached input (stable system prompt + brand context prefix) is billed at a large discount, and the Batch API halves prices for non-urgent work (e.g. nightly analytics summaries). The estimates below **ignore** these discounts, so they are conservative.

### Estimated cost per action
| Action | Tier | Tokens (in / out) | Est. cost | Credits (1 credit = $0.01) |
|---|---|---|---|---|
| Generate 3 social posts (incl. guardrail checks, 20% regeneration rate) | Standard + Fast | ~7k / 1.8k | **$0.033** | 4 |
| Planner chat turn (routing) | Standard | ~3k / 0.5k | $0.011 | 2 |
| Campaign plan (4 weeks) | Standard | ~10k / 4k | $0.10 | 10 |
| Campaign plan (4 weeks) | Reasoning (incl. ~4k reasoning tokens) | ~10k / 8k | $0.50 | 50 |
| WhatsApp support reply | Standard + Fast | ~5k / 0.3k | $0.015 | 2 |
| Analytics summary | Standard | ~8k / 1k | $0.026 | 3 |
| Index a 20-page document | Embedding | ~15k / — | $0.0003 | 1 (minimum) |
| Knowledge search (query embedding) | Embedding | < 100 / — | ~$0 | 0 |

**Finding:** the Reasoning tier costs about **5×** the Standard tier for a campaign plan. Recommend starting the **Planner and Campaign agents on the Standard tier** and moving to Reasoning only if the evaluation suite shows a clear quality gain. This choice drives AI cost more than anything else.

---

## 4. AI Cost per Tenant (monthly)

| Usage profile | Posts generated | Campaign plans | Planner turns | Support replies | Analytics | **AI cost** | Credits used |
|---|---|---|---|---|---|---|---|
| Light (typical Starter) | 30 | 1 | 30 | 0 | 2 | **≈ $0.85** | ~85 |
| Typical (Growth) | 120 | 4 | 100 | 500 | 10 | **≈ $10.50** | ~1,050 |
| Heavy (Growth, busy WhatsApp) | 360 | 10 | 300 | 2,000 | 30 | **≈ $39** | ~3,900 |
| Typical with Reasoning-tier campaigns | 120 | 4 | 100 | 500 | 10 | ≈ $12 | ~1,200 |

WhatsApp support replies dominate AI cost for tenants that use the Support Agent.

---

## 5. Third-Party Fees per Tenant

### Stripe (paid by the platform, per payment)
| Fee | Rate | Confidence |
|---|---|---|
| Card processing (US domestic cards) | ~2.9% + $0.30 | ⚠️ verify for your country |
| Stripe Billing (pay-as-you-go) | 0.7% of billing volume | ✅ |
| Stripe Tax (pay-as-you-go) | 0.5% in jurisdictions where registered | ✅ |
| International cards / currency conversion | extra ~1.5% / ~1% | ⚠️ verify |
| **Typical total** | **≈ 4.1% + $0.30 per invoice** (≈ 6.5% for international cards) | |

Annual billing reduces the fixed $0.30 per-invoice cost by 12×.

### WhatsApp (paid by the tenant, if tenants pay Meta directly — recommended)
- Billing is **per delivered message**; receiving messages is free. ✅
- From **1 October 2026**, each business phone number gets **1,000 free service messages per month**; service replies beyond that are billed (reported around **$0.0034 per message in the US**; rates vary by country). ⚠️ verify on Meta's pricing page
- Marketing template messages cost more (reported ~$0.025 in the US). ⚠️ verify
- A tenant sending 2,000 support replies/month would pay Meta roughly $3–4 on top of the platform subscription. If the platform pays Meta instead, add this to the heavy-profile tenant cost.

---

## 6. Unit Economics (illustrative)

Fixed cost ≈ **$480/month** (Section 2). Example: 100 paying tenants (70 Starter-light, 30 Growth-typical), monthly billing.

| Item | Monthly |
|---|---|
| Fixed infrastructure and services | $480 |
| AI usage: 70 × $0.85 + 30 × $10.50 | $375 |
| **Total platform cost** | **$855** (≈ $8.55 per tenant) |

### Minimum plan price for a target gross margin
For a tenant using its **full credit allowance**, with Stripe ≈ 4.1% + $0.30 and a fixed-cost share of $5/tenant:

`minimum price = (AI cost at full allowance + $5 + $0.30) ÷ (1 − 0.041 − target margin)`

| Credit allowance | AI cost at full use | Min. price @ 75% margin | Min. price @ 80% margin |
|---|---|---|---|
| 500 credits | $5 | ≈ $49 | ≈ $65 |
| 1,500 credits | $15 | ≈ $97 | ≈ $128 |
| 3,000 credits | $30 | ≈ $169 | ≈ $222 |
| 5,000 credits | $50 | ≈ $265 | ≈ $348 |

Most tenants will not use their full allowance (typical Growth ≈ 1,050 credits), so real margins will be higher; this table protects against the worst case. Credit **top-up packs** should be priced at ≥ 3× AI cost (e.g. 1,000 credits = $10 AI cost → sell at ≥ $30).

---

## 7. One-Time Costs (before launch)

| Item | Estimate | Confidence |
|---|---|---|
| Penetration test (web app + API, external firm) | $5,000–15,000 | ⚠️ market range, get quotes |
| Legal: terms of service, privacy policy, DPA templates | $1,000–5,000 | ⚠️ get quotes |
| UI/UX design (wireframes + design system) | Depends on freelancer/agency | Not estimated |
| Meta Business Verification and App Review | $0 (time cost: weeks) | ✅ |
| Domain registration (first year) | ~$80 | ⚠️ verify |
| Load testing (k6 OSS from staging) | $0 | ✅ |

---

## 8. Scaling Plan and Cost Triggers

| Stage | Trigger | Change | Added monthly cost (approx.) |
|---|---|---|---|
| **Phase 1: Single server** | Launch – ~200 tenants | As in Section 2 | — |
| **Phase 2: Split data tier** | RAM > 75% sustained, MongoDB cache misses rising, Qdrant > ~1M vectors, or p95 API latency over target | Move MongoDB + Qdrant to a second 32 GB server; app server keeps API, web, Redis, SeaweedFS | + ~€140 |
| **Phase 3: Multi-node** | > ~1,000 tenants, or 99.9% availability commitment | Load balancer, 2+ app nodes, MongoDB replica set (3 nodes), Qdrant replication, SeaweedFS distributed or S3-compatible object storage, separate monitoring node | + ~€600–900 ⚠️ estimate |

Alternative at Phase 3: managed services (MongoDB Atlas, Qdrant Cloud, S3) cost more but reduce operations work; compare against the cost of a part-time DevOps engineer.

---

## 9. Cost Monitoring
- Grafana dashboard: AI cost per day, per tenant, per agent and per model (from `agentRuns`/`usage`); infrastructure utilization.
- Alerts: platform AI spend > daily cap; any tenant whose AI cost exceeds its subscription revenue for the period (Billing and Subscriptions Design.md).
- Monthly review: actual vs this estimate; update credits-per-action table after the Phase 5 model benchmark.

## Open Decisions
- Hosting provider and region (data residency for target customers).
- Model per tier after the benchmark — especially whether Planner/Campaign need the Reasoning tier.
- Plan prices and credit allowances (use Section 6).
- Whether staging runs 24/7.

## Sources (November 2026; third-party unless noted)
- OpenAI pricing summaries: [nOps](https://www.nops.io/blog/openai-api-pricing/), [CloudZero](https://www.cloudzero.com/blog/openai-pricing/), [BenchLM](https://benchlm.ai/openai/api-pricing), [Proxidize](https://proxidize.com/blog/openai-api-pricing/) — confirm at openai.com/api/pricing
- Hetzner 2026 prices: [Northflank](https://northflank.com/blog/hetzner-cloud-server-price-increases), [webhosting.today](https://webhosting.today/2026/06/18/hetzners-price-increases-reached-209-the-30-headline-applied-to-a-different-tier/), [DevShot](https://devshot.co/blog/hetzner-cloud-pricing)
- WhatsApp pricing: [Bigin](https://www.bigin.com/articles/whatsapp-business-api-pricing-update.html), [ZEPIC](https://help.zepic.com/support/solutions/articles/51000530573-whatsapp-business-platform-pricing-changes-what-you-need-to-know-before-october-1-2026), [Blueticks](https://blueticks.co/blog/whatsapp-business-api-pricing-2026)
- Stripe: [Stripe Tax pricing (Stripe)](https://support.stripe.com/questions/understanding-stripe-tax-pay-as-you-go-pricing), [Orb](https://www.withorb.com/blog/stripe-billing-pricing), [Checkout Page](https://checkoutpage.com/blog/stripe-processing-fees)
- Email: [Amazon SES pricing (AWS)](https://aws.amazon.com/ses/pricing/), [Activepieces – Postmark](https://www.activepieces.com/blog/postmark-pricing-plans-costs-by-volume-2026-guide.md)
