# Decision Register

## Purpose
One list of every decision made or still open across the design documents, with a recommendation, the sprint by which it is needed (Sprint Roadmap.md) and where it is described. Update the status here and in the source document when a decision is taken.

Status: **Decided** (default chosen in the documents — can be overruled) · **Open** (needs an owner's decision)

---

## Open Decisions — by deadline

| # | Decision | Recommendation | Needed by | Owner | Source |
|---|---|---|---|---|---|
| D1 | Transactional email provider | Postmark (simple, strong deliverability) or Amazon SES (cheapest) | S1 | Tech lead | User Stories → Open Points; Infrastructure Costing |
| D2 | MediatR licence (commercial from v13) | **Sprint 1 default: pinned MediatR 12.5.0 (Apache 2.0).** Confirm, or move to v13 licence / source-generated `Mediator` | S1 | Tech lead | CQRS_MediatR_Design |
| D3 | Assertion library | **Sprint 1 default: FluentAssertions 7.2.0 (Apache 2.0).** Do not upgrade to 8+ without a licence | S1 | Tech lead | Test Strategy → Tooling Note |
| D4 | Mocking library | **Sprint 1 default: NSubstitute** | S1 | Tech lead | Test Strategy |
| D5 | ASP.NET Identity on MongoDB | **Sprint 1 default: custom `MongoUserStore`** (password, email, security stamp, lockout stores) — no third-party package | S1 | Tech lead | AspNetCore_Clean_Architecture |
| D28 | Target framework | **Decided: .NET 10 LTS** — .NET 9 reached end of support (Nov 2026); .NET 11 is STS | S1 | Tech lead | System Architecture Document |
| D6 | Hosting provider and region | EU or India region close to target customers; compare Hetzner (post-2026 prices) with alternatives | S1 | Founder / DevOps | Infrastructure Costing |
| D7 | Data residency requirements (e.g. EU-only processing) | Decide from target market; affects hosting and AI provider region | S1 | Founder | AI Integration Design |
| D8 | Dedicated QA engineer from S1 | Yes — isolation and RBAC suites start in S1 | S1 | Founder | Test Strategy |
| D9 | Sprint start date and confirmed team | 2-week sprints, team as in roadmap assumptions | S1 | Founder | Sprint Roadmap |
| D10 | UI/UX designer engagement | Engage before S2; prototype of core flows before S5 | S2 | Product owner | UI Screen Flows and Wireframes |
| D11 | Hybrid (keyword) search and reranker | Not at first; add if retrieval recall@8 < 0.85 | S4 | AI/ML owner | Knowledge Base Design |
| D12 | Multilingual embeddings | Confirm embedding model covers all brand languages (e.g. Hindi) in S4 evaluation | S4 | AI/ML owner | Knowledge Base Design |
| D13 | Model per tier | Start Planner and Campaign on **Standard** tier; Reasoning only if evaluation shows a clear gain (5× cost) | S6 | AI/ML owner | AI Integration Design; Infrastructure Costing |
| D14 | Fallback AI provider | Anthropic Claude as secondary provider | S6 | Tech lead | AI Integration Design |
| D15 | Image generation in MVP | No — media suggestions/prompts only in MVP; revisit after launch | S5 | Product owner | Agent Specification |
| D16 | Do "Preview voice" and regenerations consume credits | Yes, show cost before running | S5 | Product owner | UI Screen Flows |
| D17 | WhatsApp messaging billing | Tenants pay Meta directly (own WABA payment method) | S8 | Founder | Social Integrations Design |
| D18 | WhatsApp auto-reply default | Off until quality is proven | S8 | Product owner | Social Integrations Design |
| D19 | Plan prices, credit allowances, top-up packs, credit value | 1 credit = $0.01; use minimum-price table; top-ups ≥ 3× AI cost | S9 | Founder | Billing design; Infrastructure Costing → Unit Economics |
| D20 | Payment provider and currencies | Stripe (USD); add Razorpay (INR) if Indian customers are a primary market | S9 | Founder | Billing and Subscriptions Design |
| D21 | Credit overage model | Hard stop at 0 credits + top-ups | S9 | Founder | Billing and Subscriptions Design |
| D22 | Extra seats as paid add-on | Upgrade-only in MVP | S9 | Founder | Billing and Subscriptions Design |
| D23 | Permanent free plan after trial | Trial only in MVP | S9 | Founder | Billing and Subscriptions Design |
| D24 | Penetration test provider and budget | Book in S7 for S10 ($5k–15k range) | S7 | Founder | Test Strategy; Infrastructure Costing |
| D25 | Pilot customer UAT as formal launch gate | Yes, 2–3 pilots in S11 | S10 | Product owner | Test Strategy |
| D26 | Staging running 24/7 | Office hours only after S10 | S10 | DevOps | Infrastructure Costing |
| D27 | Analytics data retention and CSV export in MVP | 13 months retention; CSV export post-MVP | S8 | Product owner | User Stories → Open Points |

## Post-MVP Decisions
| # | Decision | Source |
|---|---|---|
| P1 | Next social channel after Meta (LinkedIn, X, TikTok) | Social Integrations Design |
| P2 | Facebook/Instagram comments and DMs handled by Support Agent | Social Integrations Design |
| P3 | Website crawling and OCR for scanned PDFs in the knowledge base | Knowledge Base Design |
| P4 | Portal languages beyond English | User Stories |
| P5 | Mobile app vs responsive-only | UI Screen Flows |
| P6 | Dark mode | UI Screen Flows |
| P7 | Managed services (MongoDB Atlas, Qdrant Cloud, S3) at scaling Phase 3 | Infrastructure Costing |

---

## Decided (defaults chosen in the documents)
| # | Decision | Where |
|---|---|---|
| A1 | All API routes versioned under `/api/v1` | OpenAPI Specification |
| A2 | Agent set: Planner, Content, Campaign, Social, Support, Analytics | Agent Specification |
| A3 | Angular portal owns login/signup; public website redirects | NextJs_Public_Website_Architecture |
| A4 | Every published item requires human approval; auto-approve off by default | Agent Specification |
| A5 | Agents never publish; deterministic jobs publish | Agent Specification; Social Integrations Design |
| A6 | Provider-agnostic LLM gateway on Microsoft.Extensions.AI; OpenAI primary | AI Integration Design |
| A7 | Prompts versioned in Git; tenants cannot edit system prompts | AI Integration Design |
| A8 | One shared Qdrant collection with tenant-keyed payload index and enforced filters | Knowledge Base Design |
| A9 | Brand profile injected directly into prompts, not embedded | Knowledge Base Design |
| A10 | Channel tokens encrypted with ASP.NET Data Protection | Social Integrations Design |
| A11 | Credits as append-only ledger with reservations before AI calls | Billing and Subscriptions Design |
| A12 | Non-payment and downgrade never delete data; read-only instead | Billing and Subscriptions Design |
| A13 | Production server 8 dedicated vCPU / 32 GB (original 8 GB spec too small) | Infrastructure Costing |
| A14 | 2-week sprints, 11 sprints (22 weeks) to launch; website in parallel | Sprint Roadmap |
| A15 | Tenant isolation, RBAC, entitlement and idempotency suites block every merge | Test Strategy |
| A16 | Password hashing with Identity's built-in PBKDF2 hasher (not a separate BCrypt) | Security Architecture |
| A17 | Portal and API on the same origin (`app.marketingstudio.ai/api/v1`); refresh token in HttpOnly SameSite=Strict cookie | System Architecture Document; Security Architecture |
| A18 | Super Admin accounts require MFA from launch | Security Architecture |
| A19 | Data volumes on encrypted disks (MongoDB Community has no native encryption at rest) | Security Architecture; DevOps Deployment Guide |
| A20 | Backward-compatible migrations and rolling API restarts; rollback by redeploying previous image | DevOps Deployment Guide |
