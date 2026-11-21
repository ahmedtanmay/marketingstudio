# Security Architecture

## Purpose
Security controls for AI Marketing Studio. Target baseline: **OWASP ASVS Level 2** (User Stories and Acceptance Criteria.md → Non-Functional Requirements). Testing of these controls is defined in Test Strategy.md.

## Key Threats
| Threat | Main controls |
|---|---|
| Cross-tenant data access | Multi-Tenant Design.md choke points + isolation test suite |
| Account takeover | Strong password policy, lockout, rotating refresh tokens, MFA (post-MVP) |
| Unauthorized publishing to a customer's social accounts | Approval workflow, RBAC, encrypted channel tokens, audit log |
| Prompt injection via documents or customer messages | Untrusted-content delimiting, tool scoping, guardrails (AI Integration Design.md) |
| Leaked secrets / tokens | Docker Secrets, encryption at rest, log scrubbing, secret scanning |
| Forged webhooks | Signature verification (Meta, Stripe) |
| Malicious uploads | Content-type sniffing, size limits, ClamAV, no execution of uploaded files |
| Abuse / cost attacks | Rate limits, credits, per-tenant concurrency, platform daily AI spend cap |

---

## Authentication
- **ASP.NET Core Identity** with a MongoDB user store (Decision Register D5).
- **Password hashing:** Identity's built-in `PasswordHasher` (PBKDF2). A separate BCrypt implementation is not needed. *(Replaces the earlier "BCrypt" note.)*
- **Password policy:** ≥ 10 characters, checked against a common/breached password list; no forced periodic rotation.
- **Access token:** JWT, 15 minutes, signed with an asymmetric key (RS256/ES256) held in Docker Secrets; claims: `sub`, `tenantId`, roles, `jti`. No sensitive data in claims.
- **Refresh token:** opaque, 7 days, stored hashed in MongoDB, sent as `HttpOnly; Secure; SameSite=Strict` cookie scoped to `/api/v1/auth`; **rotated on every use**; reuse of a rotated token revokes the whole token family. Absolute session lifetime 30 days.
- **Lockout:** 5 failed attempts in 15 minutes → 15-minute lock; generic error messages (no account enumeration).
- **Email verification** required before connecting channels or publishing.
- **Password reset:** single-use token, 1-hour expiry; reset revokes all sessions.
- **MFA:** TOTP in Phase 2 after launch (AUTH-7); Super Admin accounts require MFA **from launch**.

## Authorization
- **RBAC** roles: Super Admin (platform), Tenant Admin, Marketing Manager, Content Manager, Viewer. Full matrix: RBAC Matrix.md.
- Implemented as ASP.NET Core **policies** per permission (e.g. `content.approve`, `channels.manage`), mapped from roles; endpoints declare the policy, never role names.
- Resource checks (does this brand/item belong to the caller's tenant?) happen in repositories (Multi-Tenant Design.md).
- Super Admin endpoints are under `/api/v1/admin/*`, require MFA, and are audit-logged.

## API and Web Security
- **TLS 1.2+** only; HSTS (with preload after launch); HTTP → HTTPS redirect at Nginx.
- **Security headers:** Content-Security-Policy (no inline scripts in the portal), X-Content-Type-Options, Referrer-Policy, Permissions-Policy, frame-ancestors 'none'.
- **CORS:** portal is same-origin; the website origin is allowed only for public endpoints (plans, lead forms).
- **CSRF:** access token sent as bearer header (not a cookie); refresh endpoint protected by SameSite=Strict + origin check.
- **Input validation:** FluentValidation on every command; request size limits; JSON only.
- **Rate limiting:** 100 req/min per user, 1,000/min per tenant, 10/min on auth endpoints per IP (Nginx + ASP.NET Core rate limiter with Redis).
- **SSRF:** the platform makes outbound calls only to configured hosts (LLM providers, Meta, Stripe, email); agents have no generic HTTP tool; a website-crawl feature (post-MVP) must use an allow-list proxy.
- **File uploads:** see Knowledge Base Design.md → Validate and scan; files are served only through short-lived signed API links (`/api/v1/media/files/{token}`); the type is detected from the bytes and only JPEG/PNG/WebP images are accepted for media.

## AI-Specific Security
- Retrieved documents and inbound customer messages are wrapped as untrusted data; system prompt forbids following instructions in them.
- Tools enforce tenant/brand from run context and validate arguments; write tools are limited to drafts — nothing an agent does is published without approval.
- Output guardrails and moderation before content is shown or sent (AI Integration Design.md).
- Prompt-injection red-team suite runs nightly (Test Strategy.md).
- LLM provider terms: no training on customer data; DPA signed.

## Data Security
| Data | At rest | In transit |
|---|---|---|
| MongoDB, Qdrant, Redis, SeaweedFS volumes | Encrypted disk volumes (LUKS) on the server — MongoDB Community has no native encryption at rest | Internal Docker network; not exposed publicly |
| Channel tokens, refresh tokens | Field-level: Data Protection encryption (channel tokens), hashing (refresh tokens) | TLS to Meta |
| Backups | Encrypted before upload to off-site storage | TLS |
| Passwords | PBKDF2 hash (Identity) | TLS |

- **Secrets** (JWT signing key, DB passwords, API keys, app secrets, Data Protection master key) in **Docker Secrets**; never in images, repos or environment files committed to Git. Rotation: API keys every 90 days; signing keys yearly with overlap.
- **PII**: customer phone numbers and emails masked in logs; personal data covered by export/erasure (Multi-Tenant Design.md → Tenant Lifecycle).

## Social Channel and Webhook Security
- Social channel tokens encrypted at rest (ASP.NET Core Data Protection), never logged, never returned by the API, never sent to the LLM; Graph calls include `appsecret_proof`.
- Meta webhooks: `X-Hub-Signature-256` verified on the raw body; Stripe webhooks: `Stripe-Signature` verified; both de-duplicated by event id.

## Tenant Isolation
- `tenantId` on all tenant data; enforced in repositories and stores, not in controllers.
- API-level authorization checks plus automated cross-tenant tests on every route.
- Details: Multi-Tenant Design.md.

## Audit Logging
Append-only `auditLog` collection (retained 1 year; Super Admin and Tenant Admin can view their scope):
- Sign-ins (success/failure), lockouts, password resets, MFA changes
- User invites, role changes, deactivations
- Content approvals, rejections, scheduling, publishing, failures
- Channel connect/disconnect and token failures
- Agent runs (summary: who, which agent, credits)
- Plan changes, credit adjustments, entitlement overrides
- Super Admin access to tenant data
- Data exports and deletions

## Vulnerability Management
- CI: SAST, dependency scanning, container scanning (Trivy), secret scanning (Gitleaks); no critical/high findings at release.
- OS: unattended security updates; monthly patch window for Docker images.
- External penetration test before launch, then yearly and after major changes.
- Security contact (`security@`) and responsible-disclosure page on the website.

## Incident Response (MVP)
1. Detect (alerts, reports) → 2. Triage severity (S1 = data exposure) → 3. Contain (revoke tokens/keys, disable feature flag, block IPs) → 4. Eradicate and recover → 5. Notify affected tenants and, for personal-data breaches, the supervisory authority within 72 hours (GDPR) → 6. Post-incident review.
Runbooks: leaked secret rotation, compromised account, cross-tenant exposure, Meta token compromise.

## Compliance
- GDPR: DPA for customers, sub-processor list (hosting, LLM providers, Meta, Stripe, email), data export and erasure within 30 days, privacy policy.
- Meta Platform Terms and WhatsApp Business policies (opt-in, template rules).
- PCI DSS: out of scope beyond SAQ-A (Stripe-hosted payment pages).
