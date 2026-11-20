# Test Strategy

## Purpose
Defines how AI Marketing Studio is tested: test levels, tools, environments, test data, the mandatory cross-cutting suites (tenant isolation, RBAC, entitlements, idempotency, AI quality), CI quality gates and release criteria. Acceptance criteria come from User Stories and Acceptance Criteria.md; this document defines how they are verified.

## Objectives
1. Prove that **no tenant can ever see or affect another tenant's data** (highest-risk defect class).
2. Prove that **nothing is published to a customer's social account without approval** and nothing is published twice.
3. Prove that **plan limits and AI credits cannot be bypassed or overspent**.
4. Keep AI output quality from regressing when prompts or models change.
5. Give fast, reliable feedback: most tests run in minutes on every pull request.

## Risk-Based Priorities
| Risk | Impact | Test focus |
|---|---|---|
| Cross-tenant data leak (MongoDB, Qdrant, MinIO, cache) | Critical | Isolation suite on every PR |
| Unapproved / duplicate / wrong-account publishing | Critical | Approval workflow + publishing idempotency tests |
| Credit overspend / quota bypass | High | Entitlement + concurrency tests |
| Off-brand or false AI content | High | AI evaluation suite + guardrail tests |
| Leaked tokens / secrets | High | Security tests, secret scanning, log assertions |
| Missed or duplicated webhooks (Stripe, Meta) | High | Webhook idempotency and ordering tests |
| Billing state wrong after payment events | High | Stripe Test Clock scenarios |
| Performance under load | Medium | Load tests before launch and before major releases |

---

## Test Levels

```text
            ┌───────────────┐
            │  Manual / UAT │   exploratory, release sign-off
           ┌┴───────────────┴┐
           │   E2E (Cypress) │   ~40 critical user journeys
          ┌┴─────────────────┴┐
          │  API / Integration │  endpoints + real infra in containers
         ┌┴───────────────────┴┐
         │        Unit          │  domain, handlers, validators, components
         └──────────────────────┘
   + specialised suites: AI evaluation · security · performance · accessibility · DR
```

### 1. Unit tests
| Area | Tools | What |
|---|---|---|
| Backend (Domain, Application, AgentEngine) | xUnit, NSubstitute or Moq, assertion library (see Tooling note) | Domain rules, MediatR handlers, FluentValidation validators, pipeline behaviors, chunker, guardrail rules, credit ledger math, lifecycle state machines, error classification |
| Angular portal | Jest + Angular Testing Library | Components, NgRx reducers/selectors/effects, guards, interceptors, pipes |
| Next.js website | Jest (or Vitest) + React Testing Library | Components, form validation, metadata helpers |

Rules: no network, no database, no real time (`TimeProvider` injected in .NET), run in < 2 minutes per project.

### 2. Integration tests (backend)
- **ASP.NET Core `WebApplicationFactory`** hosting the real API in-process.
- **Testcontainers** for MongoDB, Redis, Qdrant and MinIO — real engines, fresh per test run, isolated database/collection per test class.
- **WireMock.Net** stubs for external HTTP APIs: LLM providers, Meta Graph API, Stripe, email provider.
- **Fake LLM** (`FakeChatClient` implementing `IChatClient`) returning recorded responses, so agent flows are deterministic and free.
- Hangfire jobs executed synchronously in tests via a test job runner.
- Covers: every endpoint (happy path, validation errors, 401/403/404), repositories, ingestion pipeline, publishing jobs, webhook handlers, entitlement enforcement.

### 3. Contract tests
- The OpenAPI document generated from the API is compared with the committed spec in CI; unreviewed breaking changes fail the build.
- The Angular API client is generated from the same OpenAPI document, so frontend compile errors surface contract drift.
- External contracts: recorded Stripe and Meta webhook payloads are kept as fixtures and replayed; refresh them when the pinned API version changes.

### 4. End-to-end tests
- **Cypress** against the staging-like stack (Docker Compose in CI) with the Fake LLM and WireMock for Meta/Stripe; a smaller **nightly** run on staging with real sandbox services.
- ~40 journeys tagged with story IDs. Core set:

| Journey | Stories |
|---|---|
| Sign up → onboarding → create brand | AUTH-1, TEN-1, BRD-1 |
| Invite member → accept → role-restricted UI | AUTH-5, AUTH-6 |
| Upload document → Ready → test search | KB-1, KB-4 |
| Planner chat → generate posts → review → approve | AGT-1, AGT-2, CNT-1 |
| Approve → schedule → published (stubbed Meta) | SOC-3, SOC-4 |
| Publish failure → retry / reschedule | SOC-5 |
| Campaign plan → generate content for slots | CMP-1 |
| WhatsApp inbound → AI draft → human send | INB-1, INB-2 |
| Trial → checkout → upgrade → top-up | BIL-1–BIL-4 |
| Credits exhausted → run blocked → top-up → run succeeds | AGT-3, BIL-4 |
| Payment failure → grace → read-only → paid | BIL-5 |

### 5. Manual and exploratory testing
- Exploratory sessions per sprint on new features (time-boxed charters).
- UAT with 2–3 pilot customers before launch, focused on content quality and the approval workflow.
- Manual checks only where automation is impractical: Meta App Review flows, real device rendering of posts, email rendering.

---

## Mandatory Cross-Cutting Suites
These run on **every pull request** and block merge on failure.

### Tenant isolation
- Seed **two tenants with identical data** (same brand names, products, documents, FAQ text).
- For every endpoint that takes a resource id: calling it with tenant A's token and tenant B's id returns `404`, and no data changes.
- For every list/search endpoint: results contain only the caller's tenant/brand.
- Qdrant: searches with identical query text return only the caller's chunks; deletes by document only affect the caller.
- MinIO: pre-signed URLs and object keys are scoped to the caller's prefix.
- Redis: cache keys include `tenantId`; a cache hit for A is never served to B.
- Agent tools: a tool call where the model supplies another tenant's id still uses the run context's tenant.
- `tenantId` sent in body/query is ignored.
- **Coverage check:** a test enumerates all API routes via the endpoint data source and fails if a route has no isolation test (explicit allow-list for public routes).

### RBAC
- The permission table in RBAC Matrix.md is encoded as test data; a data-driven test calls each protected endpoint with each role and asserts allowed (2xx) vs denied (403).
- Adding an endpoint without an RBAC test entry fails the route-coverage check.

### Entitlements and credits
- Each entitlement key (Billing and Subscriptions Design.md) has a test: at limit → `402 PLAN_LIMIT_REACHED`; below limit → allowed.
- Concurrency: 20 parallel agent runs against a balance sufficient for 10 → at most 10 succeed (small overage allowed only for in-flight runs per design).
- Ledger: allowance reset, top-up consumption order, expiry and adjustments produce the expected balance.
- State enforcement: suspended/expired/cancelled tenants are read-only across all write endpoints.

### Idempotency and ordering
- Publishing: running `PublishPostJob` twice (and concurrently) results in exactly one stubbed Graph API publish call.
- Webhooks (Stripe, Meta): the same event delivered twice is processed once; out-of-order Stripe events end in the correct state.
- Ingestion: re-ingesting a document does not duplicate Qdrant points.

### Approval workflow
- No code path publishes an item whose status is not `approved`/`scheduled` (tested at job and endpoint level).
- Content Manager cannot approve; auto-approve off by default.

### Secrets and PII in logs
- Integration tests capture log output and assert that access tokens, refresh tokens, channel tokens, API keys and customer phone numbers never appear.

---

## AI Quality Testing
Defined in detail in AI Integration Design.md → Evaluation and Knowledge Base Design.md → Evaluation.

| Suite | Trigger | Gate |
|---|---|---|
| Deterministic agent tests (Fake LLM) | Every PR | Tool-call sequence, schema handling, guardrail branches, error handling |
| Guardrail unit tests | Every PR | Banned terms, channel limits, claim check, injection-delimiting |
| Prompt evaluation (real models, golden sets) | PR touching prompts/model config; nightly | No metric regression > 5%; guardrail pass ≥ 98% |
| Retrieval evaluation | PR touching chunking/embedding/retrieval; nightly | recall@8 ≥ 0.85 |
| Prompt-injection red-team set | Nightly + before release | 0 successful injections that change tool use or leak system prompt/other-tenant data |

Real-model suites run with a dedicated API key and a **hard monthly spend cap**; results are stored per prompt version for comparison.

---

## Non-Functional Testing

### Performance (k6)
- Scenarios mirror NFR targets (User Stories and Acceptance Criteria.md): API reads/writes, agent chat (Fake LLM with realistic latency), document ingestion, publishing burst (500 posts due in the same minute).
- **Load test** at MVP sizing (200 tenants, 100 concurrent runs) before launch; **soak test** 8 hours for memory/connection leaks; **spike test** for webhooks.
- Run on a staging server with the production hardware spec.

### Security
| Check | Tool | When |
|---|---|---|
| Static analysis | .NET analyzers + SonarQube (or similar), ESLint security rules | Every PR |
| Dependency vulnerabilities | `dotnet list package --vulnerable`, `npm audit`, Dependabot | Every PR + daily |
| Container images | Trivy | Every build |
| Secret scanning | Gitleaks | Every PR + pre-commit |
| Dynamic scan | OWASP ZAP baseline against staging | Nightly; full scan before release |
| Auth/session tests | Integration tests (lockout, token reuse, reset link expiry, enumeration) | Every PR |
| Penetration test | External provider | Before production launch, then yearly |

### Accessibility
- `cypress-axe` checks on all E2E journeys; no serious/critical violations.
- Manual keyboard-only and screen-reader pass (NVDA/VoiceOver) on core flows before launch.

### Public website
- Lighthouse CI on every website PR: performance, accessibility, SEO, best practices ≥ 90.

### Backup and disaster recovery
- Quarterly restore drill to a clean server from backups; verify RPO ≤ 1 h and RTO ≤ 4 h; result recorded.

### Browser compatibility
- E2E core set runs on Chrome and Firefox in CI; Edge and Safari checked manually before release.

---

## Test Environments
| Environment | Purpose | External services |
|---|---|---|
| Local | Developer runs unit + integration tests | Testcontainers, WireMock, Fake LLM |
| CI | All PR gates; E2E on Docker Compose stack | Same as local |
| Staging | Nightly E2E, AI eval, ZAP, load tests, UAT | Meta test app + test accounts, WhatsApp test number, Stripe test mode with Test Clocks, real LLM with capped key |
| Production | Post-deploy smoke tests only (read-only health checks + synthetic sign-in) | Real |

Staging mirrors production configuration (same Docker images, Nginx config, Graph API version), with separate secrets.

## Test Data
- **No production data outside production.** Staging uses synthetic data only.
- Seed scripts create: 3 sample brands (different industries and languages), products, knowledge documents, FAQs, and the **two-identical-tenants** fixture for isolation tests.
- Builders/factories in test code (e.g. `BrandBuilder`, `TenantFixture`) instead of hand-written JSON.
- Golden datasets for AI evaluation are versioned in the repository (`tests/AgentEval`, `tests/RetrievalEval`).
- Recorded LLM responses for the Fake LLM are refreshed when prompts change (script regenerates and a reviewer checks the diff).

---

## CI Pipeline and Quality Gates

```text
Pull request
  1. Build + lint + format check                       (~3 min)
  2. Unit tests (backend, portal, website) + coverage   (~5 min)
  3. Integration tests incl. isolation, RBAC,
     entitlements, idempotency (Testcontainers)         (~10 min)
  4. Contract check (OpenAPI diff)                      (~1 min)
  5. Security: dependencies, secrets, Trivy, SAST       (~4 min)
  6. E2E core set on Docker Compose + axe              (~12 min)
  7. Conditional: prompt eval / retrieval eval / Lighthouse CI
  → merge allowed only if all required checks pass

Main branch
  → build images → deploy to staging → smoke tests

Nightly (staging)
  → full E2E, AI eval + red-team, retrieval eval, ZAP baseline

Release candidate
  → full regression, load test (major releases), ZAP full scan, manual exploratory + browser checks
```

### Coverage targets
| Code | Line coverage |
|---|---|
| Domain + Application (backend) | ≥ 80% |
| AgentEngine guardrails, entitlement, billing ledger, publishing jobs | ≥ 90% |
| Angular state (NgRx) and services | ≥ 80% |
| UI components | ≥ 60% |

Coverage is a floor, not a goal; the mandatory suites above matter more.

### Flaky tests
- A test that fails intermittently is quarantined within 1 day (tagged, excluded from the gate, ticket created) and fixed or deleted within 1 sprint.
- E2E tests use `data-testid` selectors and wait on network/state, never fixed sleeps.

---

## Defect Management
| Severity | Definition | Example | Release rule |
|---|---|---|---|
| S1 Critical | Data leak, security breach, wrong/unapproved publishing, data loss, platform down | Tenant A sees tenant B's documents | Blocks release; hotfix immediately |
| S2 High | Core flow broken, no workaround; billing incorrect | Scheduled posts not publishing | Blocks release |
| S3 Medium | Feature impaired, workaround exists | Calendar drag-drop fails, edit works | Release allowed with known-issue note |
| S4 Low | Cosmetic, minor | Misaligned icon | Backlog |

Every S1/S2 fix includes a regression test that would have caught it.

## Release Criteria (production)
- All mandatory suites and E2E core set green on the release candidate.
- No open S1/S2 defects; S3s reviewed by product owner.
- AI eval and retrieval eval meet gates for the active prompt and model versions.
- No critical/high security findings; pen test findings (launch) resolved or risk-accepted in writing.
- Load test meets NFR targets (launch and major releases).
- Rollback plan verified (previous images available, database migrations backward-compatible).

## Traceability
- Test names/tags include the story ID (e.g. `[Trait("Story", "SOC-4")]`, Cypress `tags: ['SOC-4']`).
- A CI report lists each MVP story and its passing tests; stories without tests are flagged before release.

## Roles
| Role | Responsibility |
|---|---|
| Developers | Unit, integration, isolation/RBAC entries for their endpoints; fix their flaky tests |
| QA engineer | Test strategy upkeep, E2E suite, exploratory testing, release regression, defect triage |
| AI/ML owner | Golden datasets, eval and red-team suites, prompt release decisions |
| DevOps | CI pipeline, environments, load/DR tests, security scanning tools |
| Product owner | Acceptance of stories, UAT coordination, S3 release decisions |

## Test Focus by Phase
| Phase | Focus |
|---|---|
| 1 Foundation | Test harness (Testcontainers, WebApplicationFactory), isolation fixture, auth tests, CI pipeline |
| 2 Portal | Angular unit setup, first Cypress journeys, RBAC data-driven tests, axe |
| 3 Brands & Products | CRUD integration tests, route-coverage checks |
| 4 Knowledge Base | Extractor/chunker unit tests, Qdrant isolation, retrieval eval baseline |
| 5 Agents | Fake LLM, guardrail tests, AI eval harness, credit concurrency tests |
| 6 Social | WireMock Graph API, publishing idempotency, webhook tests, Meta sandbox E2E |
| 7 Website | Lighthouse CI, form/spam tests |
| 8 SaaS | Stripe Test Clock scenarios, lifecycle enforcement, load test, pen test, DR drill |

## Tooling Note
- **FluentAssertions** version 8 and later requires a paid licence for commercial use. Either pin to 7.x, use the community fork **AwesomeAssertions**, or use **Shouldly**. Decide before Phase 1.
- **Moq** remains usable; **NSubstitute** is an equivalent alternative. Pick one for the whole solution.
- Angular 19 defaults to Karma/Jasmine, which is deprecated; Jest is set up via a builder/preset (or Vitest as an alternative). Confirm the setup in Phase 2.

## Open Decisions
- Assertion library (see Tooling Note) and mocking library.
- Dedicated QA engineer from Phase 1, or developers own E2E until Phase 5.
- Pen-test provider and budget.
- Whether pilot customers' UAT is a formal launch gate.
