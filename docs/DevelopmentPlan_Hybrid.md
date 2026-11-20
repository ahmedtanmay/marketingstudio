# Full Development Plan

Scope per phase. **Timing, sprints, milestones and team assumptions are in Sprint Roadmap.md** (2-week sprints, 22 weeks to launch).

## Solution Structure

marketingstudio.ai
├── Website (Next.js)
│   ├── Landing Pages
│   ├── Blog
│   ├── Pricing
│   ├── Documentation
│   └── Signup
│
└── app.marketingstudio.ai
    └── Angular Portal
        ├── Dashboard
        ├── Brands
        ├── Products
        ├── Knowledge Base
        ├── Campaigns
        ├── Content (Review & Calendar)
        ├── AI Agents
        ├── Inbox
        ├── Analytics
        └── Settings (Users, Channels, Billing)

## Phase 1 - Foundation (2 Weeks · Sprint 1)
- ASP.NET Core Solution Setup
- MongoDB Setup
- JWT Authentication (sign up, sign in, refresh, reset, email verification)
- Tenant + trial creation at signup
- Docker Infrastructure, CI pipeline, test harness
- Start Meta Business Verification and App Review (long lead time; blocks Phase 6)

## Phase 2 - Angular Application Portal (2 Weeks · Sprint 2)
- Angular Workspace
- Angular Material
- Authentication
- Dashboard Layout
- User Management (invites, roles)

## Phase 3 - Brand & Product Management (2 Weeks · Sprint 3)
- Brand Module
- Product Module
- Media Upload
- Onboarding Checklist

## Phase 4 - Knowledge Base (2 Weeks · Sprint 4)
- MinIO Integration
- Document Ingestion Pipeline (extract, chunk, virus scan)
- Qdrant Integration
- Embeddings
- Semantic Search
- Tenant Isolation Tests
- Details: see Knowledge Base Design.md

## Phase 5 - AI Agent Engine (4 Weeks · Sprints 5–6)
- LLM Gateway, Prompt Registry, Usage Meter (AI Integration Design.md)
- Entitlement Service and Credit Ledger (Billing and Subscriptions Design.md)
- Planner Agent
- Content Agent
- Campaign Agent
- Analytics Agent
- Content Approval Workflow
- AI Evaluation Harness

Note: the Social Agent and Support Agent are delivered in Phase 6 with the channels they depend on.

## Phase 6 - Social Integrations (4 Weeks · Sprints 7–8)
- Social Agent
- Facebook
- Instagram
- WhatsApp + Support Agent + Inbox
- Publishing Scheduler and Content Calendar
- Insights Sync and Analytics Dashboard
- Details: see Social Integrations Design.md

## Phase 7 - Next.js Marketing Website (in parallel · Sprints 6–8)
- Landing Pages
- Pricing (from billing API)
- Blog
- SEO Optimization
- Lead Capture Forms

## Phase 8 - SaaS Platform Features (2 Weeks · Sprint 9)
- Subscriptions
- Tenant Management
- Billing
- Usage Tracking
- Super Admin Tools
- Details: see Billing and Subscriptions Design.md

## Phase 9 - Hardening and Launch (4 Weeks · Sprints 10–11)
- Load, soak and spike tests
- Penetration test and fixes
- DR restore drill; production provisioning and alerting
- Accessibility pass, full regression
- Pilot customers (UAT) and production launch
- Details: see Test Strategy.md → Release Criteria

## Deployment
- Ubuntu
- Docker Compose
- Nginx
- ASP.NET Core
- Angular
- Next.js
- MongoDB
- Redis
- Qdrant
- MinIO
- ClamAV
- Hangfire (in API process)
