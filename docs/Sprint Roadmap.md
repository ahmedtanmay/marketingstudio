# Sprint Roadmap

## Purpose
The single delivery schedule for the MVP. It merges DevelopmentPlan_Hybrid.md (phases) with the sprint plan and the work defined in the design documents. **This document is authoritative for timing;** the development plan describes scope per phase.

## Assumptions (change these and re-plan)
| Assumption | Value |
|---|---|
| Sprint length | **2 weeks** |
| Start date | **TBD** — week numbers are relative to Sprint 1, week 1 |
| Team | 2 backend (.NET), 2 frontend (1 Angular, 1 Angular/Next.js), 1 QA, 0.5 DevOps, product owner; AI/ML ownership by a senior backend developer |
| Total | **11 sprints = 22 weeks** to production launch |

### Why 22 weeks, not 20
The original plan (8 phases, 20 weeks) did not include the content approval workflow, WhatsApp inbox, entitlement/credit metering, AI evaluation, hardening (load test, penetration test, DR drill), or pilot/UAT time. The website (Phase 7) now runs **in parallel** (Sprints 6–8) with the second frontend developer, which absorbs part of the added scope.

---

## Timeline Overview

```text
Week:      1-2   3-4   5-6   7-8   9-10  11-12 13-14 15-16 17-18 19-20 21-22
Sprint:    S1    S2    S3    S4    S5    S6    S7    S8    S9    S10   S11
Phase 1    ███
Phase 2          ███
Phase 3                ███
Phase 4                      ███
Phase 5                            ███   ███
Phase 6                                        ███   ███
Phase 7 (website, parallel)              ███   ███   ███
Phase 8                                                    ███
Hardening                                                        ███
Pilot & launch                                                         ███
Domain + Meta Business Verification (external)  ███ ··· (start by S5, done by S7)
Meta App Review (external)                                  ███   ███ ··· (submit when publishing works)
```

## Milestones
| Milestone | End of | Meaning |
|---|---|---|
| M1 Walking skeleton | S2 | Sign up, sign in, invite users, empty portal deployed to staging via CI |
| M2 Brand data ready | S4 | Brands, products and knowledge base usable; isolation suite green |
| M3 AI content | S6 | Generate → review → approve content with metering and guardrails; **internal demo** |
| M4 Publishing | S8 | Approved posts publish to Facebook/Instagram; WhatsApp support; website live in staging |
| M5 Feature complete | S9 | Billing live in Stripe test mode; all MVP stories done |
| M6 Launch-ready | S10 | Load test, pen test, DR drill, accessibility pass complete; no open S1/S2 defects |
| M7 Production launch | S11 | Pilot customers onboarded; public launch |

---

## Sprint Details
Story IDs refer to User Stories and Acceptance Criteria.md.

### Sprint 1 (weeks 1–2) — Foundation · Phase 1
- Solution structure (Clean Architecture), MongoDB, Redis, Docker Compose, Nginx, staging server
- CI pipeline (build, unit, integration with Testcontainers, security scans); test harness and **two-tenant isolation fixture**
- Sign up with tenant + trial creation, sign in, refresh, sign out, password reset, email verification (AUTH-1–4, TEN-3, BIL-1 backend)
- **Decisions due:** email provider, assertion/mocking libraries, MediatR licence, Identity-on-MongoDB approach. Hosting: owner will self-host on their own machine with a public IP — setup postponed (D6); staging deploy follows once that machine is ready
- **External:** Stripe account in test mode (activation not needed until S9). Domain registration and Meta Business Verification are **postponed to S5** (Decision Register D29, D30); development uses Meta test users/Pages and `localhost` until then

### Sprint 2 (weeks 3–4) — Portal Shell & Users · Phase 2
- Angular workspace, Material theme, layout, routing, guards, interceptors, NgRx auth/tenant state
- Sign-in/sign-up/reset UI; invite members; manage users and roles (AUTH-5, AUTH-6)
- RBAC data-driven test suite; first Cypress journeys + axe
- **Milestone M1** — done except "deployed to staging" (waits for the self-hosted server, D6)

### Sprint 3 (weeks 5–6) — Brands & Products · Phase 3
- Brand create/edit, voice configuration, brand assets/media library (SeaweedFS), brand switcher, delete (BRD-1–5)
- Products CRUD with search/filter (PRD-1)
- Onboarding checklist (TEN-1)
- **Status: done.** Deviations: SeaweedFS instead of MinIO (D31); "Preview voice" waits for the AI engine (Sprint 5); CSV product import (PRD-2) remains post-MVP

### Sprint 4 (weeks 7–8) — Knowledge Base · Phase 4
- Upload, ClamAV scan, extraction, chunking, embeddings, Qdrant with enforced filters (KB-1, KB-2)
- FAQs, product indexing via events, test search page (KB-3, KB-4)
- Qdrant isolation tests; retrieval evaluation baseline
- **Status: done** except the formal retrieval-evaluation golden set (moved to S6 with the AI evaluation harness). Deviations: in-process job queue (D32), local Ollama / CI hash embeddings (D33), status polling instead of SignalR
- **Milestone M2**

### Sprint 5 (weeks 9–10) — AI Engine & Content · Phase 5 (part 1)
- `ILlmGateway`, OpenAI adapter, Prompt Registry, Usage Meter, Polly policies
- **Entitlement service + credit ledger + reservations** (AGT-3, BIL-4 backend)
- Content Agent, guardrail pipeline, structured output (AGT-2)
- Content approval workflow and review queue (CNT-1–3)
- **Decisions due:** model per tier (initial), image generation in MVP
- **External (latest start — postponed from S1):**
  1. Choose the product/brand name and register the domain (D29)
  2. Publish a one-page site on it with privacy policy and terms (required by Meta and Stripe)
  3. Create the Meta business portfolio, verify the domain, create the Meta app (Business type)
  4. Submit Meta Business Verification (D30) — must be complete by S7; rejections can add weeks

### Sprint 6 (weeks 11–12) — Planner, Campaigns, Evaluation · Phase 5 (part 2) + Website start
- Planner Agent with streaming chat via SignalR (AGT-1), run history (AGT-4), AI-unavailable handling (AGT-5)
- Campaign Agent and campaign management (CMP-1, CMP-2)
- Analytics Agent (read-only, on available data) (ANL-2)
- AI evaluation harness, golden datasets, model benchmark → fill tier config
- **Website:** Next.js setup, home, features, layout, SEO foundations (WEB-1 start)
- **Milestone M3** (internal demo)

### Sprint 7 (weeks 13–14) — Publishing · Phase 6 (part 1) + Website
- Facebook/Instagram connect, token encryption, webhooks endpoint (SOC-1)
- Publishers, signed media links, `PublishPostJob`, scheduling, calendar, failure handling (SOC-3–6, CNT-4)
- Social Agent tools
- **Website:** pricing page from API, blog/docs (MDX) (WEB-2, WEB-4)
- **Dependency:** Meta Business Verification complete. Submit **App Review** as soon as connect + publish work on staging (screencasts per permission); development continues against test accounts while review is pending

### Sprint 8 (weeks 15–16) — WhatsApp, Insights, Analytics · Phase 6 (part 2) + Website
- WhatsApp connect (Embedded Signup), inbox, Support Agent, human reply, 24-hour window rules (SOC-2, INB-1, INB-2)
- Insights sync jobs; analytics dashboard (ANL-1)
- **Website:** contact/demo/newsletter forms, Lighthouse CI, signup handoff (WEB-3)
- **Milestone M4**

### Sprint 9 (weeks 17–18) — Billing & Administration · Phase 8
- Stripe Checkout, Customer Portal, webhooks, reconciliation (BIL-2)
- Lifecycle enforcement, plan changes, top-ups, failed payment, cancel (BIL-3–6)
- Billing UI; Super Admin tenant overview, credit adjustments, suspend (ADM-1–3)
- Stripe Test Clock E2E scenarios
- **Decisions due:** prices and credit allowances (Infrastructure Costing.md → Unit Economics)
- **Milestone M5** (feature complete)

### Sprint 10 (weeks 19–20) — Hardening
- Load, soak and spike tests at MVP sizing; fix bottlenecks
- External penetration test; fix findings
- DR restore drill; production server provisioning and monitoring/alerts
- Accessibility pass (keyboard + screen reader) on core flows
- Full regression; defect burn-down to no open S1/S2
- Legal pages (terms, privacy, DPA, sub-processors)
- **Milestone M6**

### Sprint 11 (weeks 21–22) — Pilot & Launch
- Week 21: onboard 2–3 pilot customers in production (UAT); daily triage
- Week 22: fixes, go/no-go against release criteria (Test Strategy.md), public launch, website live
- Post-launch: on-call rota, daily cost and error review for 2 weeks
- **Milestone M7**

---

## Critical Path and Risks
| Risk | Impact | Mitigation |
|---|---|---|
| Meta Business Verification / App Review delayed | Publishing (S7–S8) cannot go live | Domain + verification start no later than S5 (postponed from S1 by owner decision); develop with test accounts; launch can proceed with Facebook/Instagram as "beta" if review is pending |
| AI quality below expectations | M3 demo weak; rework of prompts | Evaluation harness in S6; start golden datasets in S4 |
| Scope growth in agents | S5–S6 overrun | Post-MVP items stay out (User Stories priorities) |
| Pen test finds serious issues | Launch delay | Security tests on every PR from S1; ZAP nightly from S2 |
| Single frontend developer bottleneck on portal | S5–S8 slip | Website developer helps portal in S9–S10 |
| Pricing undecided | Billing config blocked | Decision due S9 at latest |

## Re-planning
If the team is smaller than assumed (e.g. 1 backend + 1 frontend), expect roughly **32–36 weeks**; keep the sprint order and move the website after S9.
