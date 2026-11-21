# User Stories and Acceptance Criteria

## Purpose
Turns the requirements (BRD_Hybrid.md, SRS_Hybrid.md) and design documents into backlog items with testable acceptance criteria, plus the non-functional requirements and Definition of Done every story must meet.

## Conventions
- **ID:** `{EPIC}-{n}` (e.g. `BRD-3`). Use as the ticket key in the issue tracker.
- **Priority:** **MVP** = required for production launch; **Post** = after launch.
- **Phase:** delivery phase from DevelopmentPlan_Hybrid.md.
- **Roles:** Super Admin, Tenant Admin, Marketing Manager, Content Manager, Viewer (RBAC Matrix.md). "Member" = any tenant role.
- Acceptance criteria are written so QA can test them directly; Given/When/Then is used where the flow matters.
- Every story also meets the **Definition of Done** and **Non-Functional Requirements** at the end of this document.

## Epics
| Epic | Name | Phase |
|---|---|---|
| AUTH | Authentication & Users | 1–2 |
| TEN | Tenant & Settings | 1–2 |
| BRD | Brand Management | 3 |
| PRD | Product Management | 3 |
| KB | Knowledge Base | 4 |
| AGT | AI Agents | 5 |
| CNT | Content & Approval | 5 |
| CMP | Campaigns | 5 |
| SOC | Social Channels & Publishing | 6 |
| INB | WhatsApp Inbox | 6 |
| ANL | Analytics | 5–6 |
| WEB | Public Website | 7 |
| BIL | Billing & Subscriptions | 1, 5, 8 |
| ADM | Platform Administration | 8 |

---

## AUTH — Authentication & Users

### AUTH-1 Sign up and create a workspace — MVP, Phase 1
As a new customer, I want to sign up with my email so that I get my own workspace and a free trial.
- Given a valid name, email, password and company name, when I submit, then a user, a tenant and a 14-day trial subscription are created and I land on the portal onboarding screen.
- Password must be ≥ 10 characters and not in a common-password list; violations show inline errors.
- An email already registered returns a generic message (no account enumeration).
- A verification email is sent; unverified users can sign in but cannot connect channels or publish until verified.
- The signing-up user is Tenant Admin of the new tenant.
- `?plan=` from the website is preselected on the upgrade screen.

### AUTH-2 Sign in — MVP, Phase 1
As a member, I want to sign in so that I can access my workspace.
- Valid credentials return an access token (15 min) and a refresh token (7 days, rotating, HTTP-only secure cookie).
- Invalid credentials return a generic error; after 5 failures in 15 minutes the account is locked for 15 minutes.
- Access token contains `userId`, `tenantId`, roles; it never contains secrets.
- Successful and failed sign-ins are written to the audit log (Security Architecture.md).
- Suspended/expired tenants can still sign in (read-only access per Billing design).

### AUTH-3 Stay signed in and sign out — MVP, Phase 1
- When the access token expires, the portal silently refreshes it; the user is not interrupted.
- A refresh token can be used once; reuse of an old refresh token revokes the whole token family and signs the user out.
- Sign out revokes the refresh token; the old access token is rejected after expiry.
- After 30 days without activity the user must sign in again.

### AUTH-4 Reset password — MVP, Phase 1
- "Forgot password" always shows the same confirmation, whether or not the email exists.
- Reset link is single-use and expires after 1 hour.
- Resetting the password revokes all the user's refresh tokens.

### AUTH-5 Invite team members — MVP, Phase 2
As a Tenant Admin, I want to invite colleagues with a role so that my team can work in the workspace.
- Invite by email + role; the invitee receives a link valid for 7 days.
- Inviting beyond the plan's seat limit is blocked with an upgrade prompt (`seats.max`).
- Pending invites can be resent or revoked.
- Accepting creates the user in the inviting tenant with the chosen role.

### AUTH-6 Manage users and roles — MVP, Phase 2
- Tenant Admin can change a member's role or deactivate them; deactivated users' sessions end within 15 minutes (access token expiry) and refresh fails immediately.
- The last Tenant Admin cannot be removed or downgraded.
- Role changes are audit-logged.

### AUTH-7 Multi-factor authentication — Post
- Users can enable TOTP MFA; Tenant Admin can require MFA for all members.

---

## TEN — Tenant & Settings

### TEN-1 Onboarding checklist — MVP, Phase 2
- New tenants see a checklist: create brand → add products → upload knowledge → connect a channel → generate first post. Steps tick automatically when completed.

### TEN-2 Workspace branding — Post, Phase 2
- Tenant Admin can set logo and primary/secondary colours; the portal theme updates for all members.

### TEN-3 Tenant isolation — MVP, Phase 1 (technical)
As the platform, I must never expose one tenant's data to another.
- Every repository query is filtered by `tenantId` from the token; requests for another tenant's resource id return `404`.
- Automated tests cover every collection and the Qdrant store with two tenants holding identical data.
- `tenantId` supplied in a request body or query string is ignored.

---

## BRD — Brand Management

### BRD-1 Create a brand — MVP, Phase 3
As a Marketing Manager, I want to create a brand profile so that agents write in the right voice.
- Required: name, industry, at least one language, timezone. Optional: description, target audience, tone of voice, banned words/topics, website URL.
- Creating beyond `brands.max` is blocked with an upgrade prompt.
- Brand names are unique within a tenant.
- `BrandCreatedEvent` is emitted.

### BRD-2 Configure brand voice — MVP, Phase 3
- Tone of voice is set with presets (e.g. friendly, professional, playful) plus free-text guidelines (max 2,000 characters).
- Up to 3 example posts can be saved as voice examples.
- Banned words/topics (max 200 entries) are enforced by the output guardrails (AI Integration Design.md).
- "Preview voice" generates a sample post using the current settings without saving content.

### BRD-3 Brand assets — MVP, Phase 3
- Upload logo and images (JPEG/PNG/WebP, max 10 MB each) to the brand media library (SeaweedFS).
- Assets can be tagged and selected when editing content.

### BRD-4 Switch brand context — MVP, Phase 2
- A brand switcher in the portal header sets the active brand; lists, agents and analytics show only the active brand's data.

### BRD-5 Delete a brand — MVP, Phase 3
- Tenant Admin only; requires typing the brand name to confirm.
- Cancels scheduled posts, disconnects channels, deletes knowledge vectors and media (Knowledge Base Design.md → Updates and Deletion).

---

## PRD — Product Management

### PRD-1 Manage products — MVP, Phase 3
- CRUD for products: name, description, category, price + currency, key features, URL, images.
- List supports search by name, filter by category, pagination (25 per page).
- Create/update re-indexes the product into the knowledge base within 1 minute.

### PRD-2 Bulk import products — Post, Phase 3
- Import from CSV (template downloadable); invalid rows are reported with row number and reason; valid rows are imported.

---

## KB — Knowledge Base

### KB-1 Upload documents — MVP, Phase 4
As a Content Manager, I want to upload brand documents so that agents use accurate information.
- Accepts PDF, DOCX, TXT, MD, HTML up to the plan's `kb.maxFileBytes`; other types are rejected with a clear message.
- Given a valid upload, when processing finishes, then status shows **Ready** with chunk count; progress updates live without page refresh.
- Infected files are rejected and removed; scanned PDFs without text fail with "OCR not supported".
- Uploading the same file twice to a brand is rejected as a duplicate.
- Limits `kb.docsPerBrand` and `kb.storageBytes` are enforced.

### KB-2 Manage documents — MVP, Phase 4
- List documents with status, size, uploaded by, date. Failed documents show the reason and a Retry button.
- Replacing a document re-indexes it; deleting removes it from search within 1 minute.

### KB-3 FAQs — MVP, Phase 4
- Create/edit/delete Q&A pairs per brand; changes are searchable within 1 minute.

### KB-4 Test search — MVP, Phase 4
- A search box shows the top results an agent would receive for a question, with source and relevance score; results come only from the active brand.

---

## AGT — AI Agents

### AGT-1 Chat with the Planner — MVP, Phase 5
As a Marketing Manager, I want to describe what I need in plain language so that the right agent does the work.
- Responses stream into the chat.
- The Planner either answers, asks one clarifying question, or runs specialist agents; the UI shows which agents ran.
- Only agents allowed by the plan (`agents.allowed`) are used; requesting another shows an upgrade message.
- Chat history is saved per brand in conversations.

### AGT-2 Generate social posts — MVP, Phase 5
As a Content Manager, I want to generate posts for a channel so that I can publish faster.
- Inputs: channel, content type, count (1–10), optional products, language, optional campaign.
- Given valid input, when generation completes, then N content items are created in **Pending review**, each with text, hashtags, call to action and a media suggestion.
- Output respects channel length rules and contains no banned words.
- Product facts (price, features) match product data; items with no supporting knowledge are flagged.
- Each item shows the knowledge sources it used.
- Typical 3-post request completes in ≤ 20 seconds (p95).

### AGT-3 Credit check before running — MVP, Phase 5
- If the tenant lacks enough credits, the run is rejected before calling the AI, with a message showing balance and a top-up link.
- Credits used are shown on each completed run.

### AGT-4 Run history — MVP, Phase 5
- Tenant Admin and Marketing Manager can list agent runs (agent, user, status, credits, duration) for the plan's retention period and open a run to see input and output.

### AGT-5 AI unavailable — MVP, Phase 5
- When the AI provider is down and the fallback is also unavailable, the user sees "AI temporarily unavailable — your request is queued" and is notified when it completes.

---

## CNT — Content & Approval

### CNT-1 Review and approve content — MVP, Phase 5
As a Marketing Manager, I want to review generated content before it goes out so that nothing off-brand is published.
- A review queue lists items in **Pending review** for the active brand.
- Reviewer can edit text and media, then **Approve** or **Reject** (reason required).
- Content Manager can submit but cannot approve (RBAC).
- Every status change is audit-logged with user and time.

### CNT-2 Regenerate with feedback — MVP, Phase 5
- On a rejected or draft item, the user can enter feedback and regenerate; the new version replaces the draft and keeps the history.

### CNT-3 Guardrail flags — MVP, Phase 5
- Items that failed guardrails after one retry appear as **Draft** with the specific flags (e.g. "banned term: X", "unsupported claim"). They cannot be approved until flags are resolved or dismissed by a Marketing Manager.

### CNT-4 Content calendar — MVP, Phase 6
- Calendar view (month/week) of scheduled and published items per brand and channel; drag-and-drop to reschedule items not yet started.

### CNT-5 Auto-approve per channel — Post
- Tenant Admin can enable auto-approve for a channel; auto-approved items are marked as such in the audit log.

---

## CMP — Campaigns

### CMP-1 Plan a campaign with AI — MVP, Phase 5
As a Marketing Manager, I want an AI-drafted campaign plan so that I can launch faster.
- Inputs: goal, products, channels, start and end date (≤ 90 days), optional budget.
- Output: draft campaign with a schedule (date, channel, content brief per slot, ≤ 60 slots).
- The plan is editable; "Generate content" creates content items for selected slots in Pending review.
- Nothing is scheduled until items are approved.

### CMP-2 Manage campaigns — MVP, Phase 5
- List campaigns with status (draft, active, completed, archived), date range, channels and item counts.
- Archiving a campaign cancels its unpublished scheduled items after confirmation.

---

## SOC — Social Channels & Publishing

### SOC-1 Connect Facebook and Instagram — MVP, Phase 6
As a Tenant Admin, I want to connect my Facebook Pages and Instagram accounts so that approved content can be published.
- Connect opens Facebook Login for Business; after consent I choose Pages/Instagram accounts and assign each to a brand.
- Instagram personal accounts show guidance to convert to Business/Creator.
- Limit `channels.max` is enforced.
- Tokens are never displayed or returned by the API.

### SOC-2 Connect WhatsApp — MVP, Phase 6
- Connect opens WhatsApp Embedded Signup; on completion the number appears as an active channel for the selected brand.

### SOC-3 Schedule an approved post — MVP, Phase 6
- Only **Approved** items can be scheduled.
- User picks channel(s) and time in the brand's timezone, or accepts a suggested time.
- Instagram items without media cannot be scheduled (validation message).
- Scheduling beyond 90 days or beyond `posts.monthlyScheduled` is blocked.

### SOC-4 Publish on time — MVP, Phase 6
- Given a scheduled item and an active connection, when the scheduled time arrives, then the post is published within 2 minutes and the item shows **Published** with a link to the live post.
- A retried job never creates a duplicate post.

### SOC-5 Publishing failures — MVP, Phase 6
- Failed items show the reason in plain language and actions **Retry** / **Edit & reschedule**.
- Auth failures mark the channel **Disconnected**, pause its scheduled posts and notify Tenant Admin by email and in-app.

### SOC-6 Disconnect a channel — MVP, Phase 6
- Tenant Admin can disconnect; scheduled posts for that channel are cancelled after confirmation listing them.

---

## INB — WhatsApp Inbox

### INB-1 AI replies to customers — MVP, Phase 6 (Growth plan and above)
As a brand, I want customer WhatsApp questions answered from my knowledge base.
- Inbound messages appear in the inbox within 10 seconds.
- With auto-reply **off** (default), the AI draft waits for a human to send, edit or discard.
- With auto-reply **on**, the AI reply is sent automatically if confidence is sufficient; otherwise the conversation moves to **Awaiting human**.
- Refunds, pricing changes and legal questions always escalate.

### INB-2 Human reply — MVP, Phase 6
- Tenant Admin and Marketing Manager can reply manually; outside the 24-hour window, free-text is disabled and only approved templates can be sent.
- Conversations can be marked resolved; they auto-resolve after 24 hours of inactivity.

---

## ANL — Analytics

### ANL-1 Dashboard — MVP, Phase 6
- Dashboard widgets (Angular_Enterprise_Architecture.md): campaign performance, posts published, engagement by channel, AI credit usage, agent activity — for a selectable date range and the active brand.
- Data is at most 1 hour old for posts published in the last 7 days.

### ANL-2 Ask the Analytics Agent — MVP, Phase 5–6
- "How did campaign X perform?" returns a summary, a metrics table and up to 5 recommendations; every number matches the dashboard for the same period.

### ANL-3 Weekly report email — Post
- Tenant Admin can enable a weekly email summary per brand.

---

## WEB — Public Website

### WEB-1 Marketing pages — MVP, Phase 7
- Pages: home, features, pricing, customers, blog, docs, contact (NextJs_Public_Website_Architecture.md).
- Lighthouse ≥ 90 for performance, accessibility, SEO and best practices on home and pricing pages (mobile).
- Each page has unique title, meta description and Open Graph tags; sitemap.xml and robots.txt are generated.

### WEB-2 Pricing page — MVP, Phase 7
- Plans and limits are loaded from `GET /api/v1/billing/plans` (no hard-coded prices).
- "Start free trial" links to portal signup with `?plan=`.

### WEB-3 Lead capture — MVP, Phase 7
- Newsletter, contact and demo forms post to the API with spam protection (honeypot + rate limit); submitter receives a confirmation; sales receives a notification.

### WEB-4 Blog and docs — MVP, Phase 7
- Content authored in MDX; publishing requires no code changes outside the content folder.

---

## BIL — Billing & Subscriptions

### BIL-1 Trial — MVP, Phase 1 / 8
- New tenants start a 14-day trial with trial limits; a banner shows days remaining; reminder 3 days before the end.
- At trial end without a paid plan, the workspace becomes read-only.

### BIL-2 Subscribe — MVP, Phase 8
As a Tenant Admin, I want to choose a plan and pay so that I can keep using the platform.
- Choosing a plan opens Stripe Checkout; after payment the plan is active in the portal within 1 minute.
- Monthly and annual intervals are available.
- Invoices are emailed and listed in the portal.

### BIL-3 Change plan — MVP, Phase 8
- Upgrade applies immediately with prorated charge and new limits.
- Downgrade shows a preview of what exceeds the new limits and takes effect at period end; excess items become read-only, nothing is deleted.

### BIL-4 Credits and top-ups — MVP, Phase 5 / 8
- Credits page shows balance, usage by agent/brand/day, and ledger entries.
- Tenant Admin receives alerts at 80% and 100% of monthly allowance.
- Buying a top-up pack adds credits within 1 minute of payment.

### BIL-5 Failed payment — MVP, Phase 8
- On failed renewal the tenant enters a 7-day grace period with a banner; after it ends the workspace becomes read-only and scheduled posts pause; paying restores everything immediately.

### BIL-6 Cancel — MVP, Phase 8
- Cancellation (via Customer Portal) keeps access until period end; data is retained for 90 days with reminder emails before deletion.

---

## ADM — Platform Administration (Super Admin)

### ADM-1 Tenant overview — MVP, Phase 8
- Super Admin can list/search tenants with plan, status, MRR, credits used and AI cost.

### ADM-2 Adjust credits and entitlements — MVP, Phase 8
- Super Admin can grant credits or set enterprise overrides; a reason is required and the change is audit-logged.

### ADM-3 Suspend a tenant — MVP, Phase 8
- Super Admin can suspend a tenant for abuse; members see a suspension notice; scheduled posts pause.

### ADM-4 Support access — Post
- Super Admin can view a tenant's workspace read-only with the tenant's consent; every access is audit-logged and visible to the Tenant Admin.

---

## Non-Functional Requirements

### Performance
| Measure | Target |
|---|---|
| API read endpoints (p95, excluding AI) | ≤ 300 ms |
| API write endpoints (p95, excluding AI and uploads) | ≤ 500 ms |
| Portal initial load (broadband, cached CDN assets) | ≤ 3 s to interactive |
| Agent chat first streamed token (p95) | ≤ 3 s |
| Generate 3 posts (p95) | ≤ 20 s |
| Knowledge document (20 pages) ready | ≤ 2 min |
| Scheduled post published | within 2 min of scheduled time |
| Public website | Lighthouse ≥ 90, FCP < 2 s |

### Availability and reliability
- Portal and API availability: 99.5% monthly (MVP single-server); 99.9% target after multi-node (Infrastructure Costing.md, Phase 3).
- Backups: MongoDB daily full + hourly incremental/oplog; SeaweedFS and Qdrant daily; retained 30 days; encrypted.
- **RPO ≤ 1 hour, RTO ≤ 4 hours.** Restore tested quarterly.
- Background jobs survive API restarts (Hangfire persistent storage).

### Scalability (MVP sizing)
- 200 tenants, 1,000 brands, 2,000 users, 50,000 published posts/month, 100 concurrent agent runs — on the MVP server spec, with a load test proving it before launch.

### Security
- OWASP ASVS Level 2 controls for authentication, session, access control and input validation.
- TLS 1.2+ everywhere; HSTS on all domains.
- Secrets only in Docker Secrets; none in code or images (CI secret scanning).
- Dependency and container vulnerability scanning in CI; no critical/high findings at release.
- Penetration test before production launch.
- Rate limiting: 100 requests/min per user, 1,000/min per tenant (Redis), stricter on auth endpoints.

### Privacy and compliance
- GDPR: data export and erasure per tenant and per WhatsApp customer within 30 days of request.
- Privacy policy lists AI and messaging sub-processors.
- PII masked in logs.

### Accessibility and usability
- Portal meets **WCAG 2.1 AA** for core flows (sign-in, brand, content review, scheduling, billing).
- Supported browsers: latest two versions of Chrome, Edge, Firefox, Safari. Portal is usable from 1280 px wide; review queue and inbox usable on tablet.
- UI text externalized with ngx-translate (English at launch).

### Observability
- Structured logs (Serilog → Loki), metrics (Prometheus → Grafana), traces (OpenTelemetry) with `tenantId` and `runId` correlation.
- Alerts defined for API error rate > 2%, job failures, AI fallback, publish failures, backup failure.

---

## Definition of Done
A story is done when:
1. All acceptance criteria pass and are demonstrated to the product owner.
2. Unit tests written; coverage of new Application/Domain code ≥ 80%.
3. Integration tests cover new endpoints, including **tenant isolation** and **RBAC** (allowed and denied roles).
4. API changes are reflected in the OpenAPI spec.
5. Entitlement checks are in place for any limited resource.
6. Audit logging is added for security-relevant actions.
7. No new critical/high vulnerabilities; secrets scan clean.
8. Code reviewed and merged; CI green; deployed to staging.
9. UI meets WCAG 2.1 AA for the changed screens and all strings are externalized.
10. User-facing docs/help updated where behaviour changed.

## Open Points
- Transactional email provider (verification, invites, password reset, alerts) — not yet chosen.
- Analytics retention period and whether exports (CSV) are MVP.
- Languages beyond English for the portal UI.
