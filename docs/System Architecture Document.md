# System Architecture Document

## Overview
AI Marketing Studio is a multi-tenant SaaS platform where businesses configure brand-specific AI agents that generate, review, schedule and publish marketing content and answer customers. It uses a hybrid front end (Next.js public website + Angular application portal), an ASP.NET Core 10 (LTS) backend built with Clean Architecture and CQRS, and self-hosted data services (MongoDB, Redis, Qdrant, MinIO) on Docker, behind Nginx.

This document is the entry point: it shows how the parts fit together and links to the detailed design documents (see Document Map).

---

## System Context

```text
                       ┌──────────────────────┐
   Visitors ─────────▶ │  Public website      │
                       │  marketingstudio.ai  │
                       └──────────┬───────────┘
                                  │ signup / login redirect
                                  ▼
 Tenant users ───────▶ ┌──────────────────────────────────────┐ ◀──── Super Admin
 (Admin, Managers,     │        AI Marketing Studio            │
  Viewers)             │  app.marketingstudio.ai               │
                       └──┬──────────┬──────────┬──────────┬──┘
                          │          │          │          │
                          ▼          ▼          ▼          ▼
                     ┌────────┐ ┌─────────┐ ┌────────┐ ┌──────────┐
                     │ LLM    │ │ Meta    │ │ Stripe │ │ Email    │
                     │ APIs   │ │ (FB, IG,│ │        │ │ provider │
                     │(OpenAI,│ │WhatsApp)│ │        │ │          │
                     │fallback│ └────▲────┘ └───▲────┘ └──────────┘
                     └────────┘      │          │
                                     │ webhooks │ webhooks
 Brand customers ── WhatsApp ────────┘          │
```

## Containers

```text
                           Internet (HTTPS)
                                  │
                         ┌────────▼────────┐
                         │      Nginx      │  TLS, routing, rate limits, security headers
                         └─┬──────┬──────┬─┘
     marketingstudio.ai    │      │      │   media.marketingstudio.ai
            ┌──────────────┘      │      └──────────────────┐
            ▼                     ▼ app.marketingstudio.ai  ▼
   ┌─────────────────┐   ┌─────────────────────────────┐  ┌─────────────────────┐
   │ Next.js website │   │ /        Angular portal      │  │ MinIO pre-signed    │
   │ (SSR)           │   │          (static files)      │  │ media (read-only)   │
   └────────┬────────┘   │ /api/v1  ASP.NET Core API    │  └─────────────────────┘
            │ plans,     │ /hubs    SignalR             │
            │ lead forms │ /hangfire dashboard (admin)  │
            └───────────▶└──────────────┬──────────────┘
                                        │
         ┌──────────────┬───────────────┼───────────────┬──────────────┐
         ▼              ▼               ▼               ▼              ▼
   ┌──────────┐   ┌──────────┐    ┌──────────┐    ┌──────────┐   ┌──────────┐
   │ MongoDB  │   │  Redis   │    │  Qdrant  │    │  MinIO   │   │  ClamAV  │
   │ data +   │   │ cache,   │    │ vectors  │    │ files &  │   │ upload   │
   │ Hangfire │   │ locks,   │    │          │    │ media    │   │ scanning │
   │ storage  │   │ limits   │    │          │    │          │   │          │
   └──────────┘   └──────────┘    └──────────┘    └──────────┘   └──────────┘
         Monitoring: Prometheus · Grafana · Loki (internal network only)
```

- The **API process** hosts REST endpoints, SignalR hubs, Hangfire job servers (ingestion, publishing, insights, billing reconciliation) and the Agent Engine.
- The portal and API share the `app.marketingstudio.ai` origin, so the refresh-token cookie is first-party and no CORS is needed for the portal.
- Only Nginx is exposed to the internet; all data services are on an internal Docker network.

## Backend Structure (Clean Architecture)

```text
src
├── Api              Controllers/endpoints, SignalR hubs, webhook endpoints, auth setup
├── Application      Commands/queries (MediatR), validators, DTOs, interfaces, pipeline behaviors
├── Domain           Entities, value objects, domain events, rules
├── Infrastructure   MongoDB, Redis, Qdrant, MinIO, LLM adapters, Meta/Stripe/email clients, Hangfire jobs
├── AgentEngine      Agent runtime, tool registry, prompt registry, guardrails
└── Tests            Unit, integration, evaluation suites
```
Dependencies point inward: Api → Application → Domain; Infrastructure and AgentEngine implement Application interfaces. Details: AspNetCore_Clean_Architecture.md, CQRS_MediatR_Design.md.

## Domains (bounded contexts)
| Domain | Responsibility | Main collections | Design doc |
|---|---|---|---|
| Identity | Users, sign-in, tokens, roles, invites | users | Security Architecture |
| Tenancy | Tenants, workspace settings, isolation | tenants | Multi-Tenant Design |
| Brands & Products | Brand profile, voice, assets, products | brands, products | SRS_Hybrid |
| Knowledge | Documents, FAQs, ingestion, retrieval | documents, faqs (+ Qdrant) | Knowledge Base Design |
| Agents | Agent runs, planner, tools, prompts, guardrails | agents, agentRuns, conversations | Agent Specification; AI Integration Design |
| Content | Content items, approval, calendar | contentItems | Agent Specification |
| Campaigns | Campaign plans and schedules | campaigns | Agent Specification |
| Channels | Social connections, publishing, webhooks | channelConnections | Social Integrations Design |
| Inbox | WhatsApp conversations, human handover | conversations | Social Integrations Design |
| Analytics | Insights, dashboards | analytics | Social Integrations Design |
| Billing | Plans, subscriptions, credits, entitlements | plans, subscriptions, creditLedger, usage, billingEvents | Billing and Subscriptions Design |

Domains communicate through MediatR commands/queries and **domain events** (e.g. `ProductAddedEvent` → re-index product; `ContentApproved` → Social Agent may schedule).

## Key Runtime Flows
| Flow | Path |
|---|---|
| Generate content | Portal → API → entitlement check & credit reservation → Agent Engine (brand context + Qdrant retrieval + LLM + guardrails) → contentItems (pending_review) → SignalR update |
| Publish | Approval → schedule → Hangfire delayed job → Meta Graph API → contentItems (published) → insights sync later |
| Ingest document | Upload → MinIO → Hangfire → ClamAV → extract/chunk → embeddings → Qdrant → status via SignalR |
| WhatsApp support | Meta webhook → verify signature → Hangfire → Support Agent → reply or human inbox |
| Billing | Stripe Checkout → Stripe webhook → subscription sync → entitlements cache refresh |

## Cross-Cutting Concerns
| Concern | Approach | Doc |
|---|---|---|
| Tenant isolation | Tenant context from JWT; tenant-aware repositories and stores | Multi-Tenant Design |
| Authorization | RBAC policies per endpoint | RBAC Matrix; Security Architecture |
| Plan limits | `IEntitlementService` + MediatR behavior | Billing and Subscriptions Design |
| Validation | FluentValidation pipeline behavior | CQRS_MediatR_Design |
| Resilience | Polly for all outbound calls | AI Integration Design |
| Background work | Hangfire (MongoDB storage) | DevOps Deployment Guide |
| Real-time updates | SignalR, groups per tenant/brand | Multi-Tenant Design |
| Logging/metrics/tracing | Serilog → Loki, Prometheus, OpenTelemetry | DevOps Deployment Guide |
| Audit | Append-only audit log for security and content actions | Security Architecture |

## Technology Stack
| Layer | Technology |
|---|---|
| Public website | Next.js 15+, TypeScript, Tailwind CSS, MDX |
| Portal | Angular 19+, Angular Material, NgRx, RxJS, SignalR client, Chart.js, ngx-translate |
| Backend | ASP.NET Core 10 (LTS), MediatR 12.5 (last Apache-2.0 release — Decision Register D2), FluentValidation, Mapster, Serilog, Polly, Hangfire, Microsoft.Extensions.AI |
| Data | MongoDB (self-hosted), Redis, Qdrant, MinIO |
| Security | ASP.NET Core Identity, JWT + rotating refresh tokens, Data Protection, ClamAV |
| External | OpenAI (+ fallback LLM), Meta Graph API & WhatsApp Cloud API, Stripe, transactional email |
| Infrastructure | Ubuntu 24.04, Docker Compose, Nginx, Let's Encrypt, GitHub Actions |
| Observability | Prometheus, Grafana, Loki, OpenTelemetry |

## Quality Attributes
Targets are defined in User Stories and Acceptance Criteria.md → Non-Functional Requirements (performance, 99.5% availability at MVP, RPO 1 h / RTO 4 h, WCAG 2.1 AA, OWASP ASVS L2).

## Scaling Path
Single server → split data tier → multi-node (Infrastructure Costing.md → Scaling Plan). The architecture keeps services stateless (API, website) and state in data stores, so app nodes can be added behind Nginx without code changes; Hangfire and SignalR need Redis backplane/shared storage at multi-node.

---

## Document Map
| Area | Documents |
|---|---|
| Business & requirements | BRD_Hybrid, SRS_Hybrid, User Stories and Acceptance Criteria |
| Architecture | System Architecture Document (this), AspNetCore_Clean_Architecture, CQRS_MediatR_Design, Angular_Enterprise_Architecture, NextJs_Public_Website_Architecture |
| Data & API | Database Design Document, MongoDB Collection Schemas, OpenAPI Specification |
| AI | Agent Specification, AI Integration Design, Knowledge Base Design |
| Integrations | Social Integrations Design, Billing and Subscriptions Design |
| Security & tenancy | Security Architecture, Multi-Tenant Design, RBAC Matrix |
| UX | UI Screen Flows and Wireframes |
| Delivery | DevelopmentPlan_Hybrid, Sprint Roadmap, Test Strategy, DevOps Deployment Guide, Infrastructure Costing |
| Governance | Decision Register |
