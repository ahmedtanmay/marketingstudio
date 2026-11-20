# Social Integrations Design

## Purpose
Defines how AI Marketing Studio connects tenants' Facebook Pages, Instagram accounts and WhatsApp Business numbers, publishes approved content, receives inbound messages for the Support Agent, and collects performance data for the Analytics Agent. Delivered in Phase 6.

> **Verify before build:** Meta's permissions, rate limits, publishing limits and WhatsApp pricing change regularly. Every limit marked *(verify)* must be checked against the current Meta developer documentation at implementation time. The Graph API version is pinned in configuration and upgraded deliberately.

## Principles
- **Nothing is published without an approved `contentItem`** (Agent Specification.md → Content Approval Workflow).
- **Publishing is done by deterministic code**, not by the LLM. The Social Agent only prepares and schedules.
- **Tokens are secrets.** Stored encrypted, never logged, never sent to the LLM.
- **Idempotent publishing.** A retry must never create a duplicate post.
- **Provider-agnostic core.** Each platform is an adapter behind `IChannelPublisher`, so LinkedIn, X or TikTok can be added later.

---

## Scope

| Capability | Facebook Page | Instagram (Business/Creator) | WhatsApp Business |
|---|---|---|---|
| Connect account | MVP | MVP | MVP |
| Publish text/link post | MVP | — (image/video required) | — |
| Publish image | MVP | MVP | — |
| Publish carousel | Post-MVP | MVP | — |
| Publish video / Reels | Post-MVP | Post-MVP | — |
| Stories | Post-MVP | Post-MVP | — |
| Inbound messages → Support Agent | Post-MVP (Messenger) | Post-MVP (DMs) | MVP |
| Comments → Support Agent | Post-MVP | Post-MVP | — |
| Outbound marketing messages | — | — | Post-MVP (templates) |
| Insights → Analytics | MVP | MVP | MVP (message delivery/read stats) |

Instagram **personal** accounts are not supported by the API — users must convert to a Business or Creator account linked to a Facebook Page.

---

## Meta Platform Setup (prerequisites — start by Sprint 5)

Business Verification can take from a few days to several weeks (document rejections) and blocks going live in Phase 6. It needs a registered domain with a privacy-policy page, so both are **postponed to Sprint 5 at the latest** (Decision Register D29, D30). Development before that uses the app in Development mode with test users, test Pages, a test Instagram account and the WhatsApp test number.

**App Review** cannot be submitted early: Meta requires a screencast of each permission in use, so it is submitted once connect + publish work on staging (Sprint 7).

1. Create a Meta Business account and a Meta App (type: Business).
2. Complete **Business Verification** for the company operating the platform.
3. Configure **Facebook Login for Business** (redirect URI `https://app.marketingstudio.ai/channels/callback/meta`).
4. Configure **WhatsApp Embedded Signup** so tenants can connect their own numbers from the portal.
5. Publish privacy policy, terms and a **data deletion callback** URL (required by Meta).
6. Submit **App Review** with screencasts for each permission.

### Permissions requested *(verify exact names)*
| Permission | Why |
|---|---|
| `pages_show_list` | List the user's Pages to choose from |
| `pages_manage_posts` | Publish to Facebook Pages |
| `pages_read_engagement` | Read posts and engagement |
| `read_insights` | Page and post insights |
| `instagram_basic` | Read the linked Instagram account |
| `instagram_content_publish` | Publish to Instagram |
| `instagram_manage_insights` | Instagram media insights |
| `business_management` | Access assets owned by the tenant's Business |
| `whatsapp_business_management` | Manage WhatsApp numbers and templates |
| `whatsapp_business_messaging` | Send/receive WhatsApp messages |

Request only these; extra permissions slow App Review.

---

## Architecture

```text
Portal (Channels page)
  -> Connect flow (Facebook Login for Business / WhatsApp Embedded Signup)
     -> API: OAuth callback -> exchange code -> long-lived tokens
        -> channelConnections (encrypted tokens)

Content approved
  -> Social Agent: adapt per channel, suggest time
     -> schedule_post -> contentItems.status = scheduled
        -> Hangfire delayed job: PublishPostJob(contentItemId, channel)
           -> IChannelPublisher (FacebookPublisher | InstagramPublisher)
              -> Meta Graph API
           -> contentItems.status = published | failed

Meta Webhooks
  -> POST /api/v1/webhooks/meta  (signature verified)
     -> queue (Hangfire)
        -> WhatsApp message  -> Support Agent
        -> token/permission revoked -> mark connection disconnected
        -> account deauthorized / data deletion -> cleanup

Hangfire recurring: SyncInsightsJob -> Graph API insights -> analytics collection
Hangfire recurring: TokenHealthJob  -> validate tokens, warn before expiry
```

### Components
| Component | Layer |
|---|---|
| `IChannelPublisher`, `IChannelConnectionService`, `IInsightsCollector` | Application |
| Connect/Disconnect commands, Schedule/Cancel commands | Application |
| `MetaGraphClient`, `FacebookPublisher`, `InstagramPublisher`, `WhatsAppClient`, webhook handler, Hangfire jobs | Infrastructure |
| `schedule_post`, `get_channel_connection`, `suggest_post_time` tools | AgentEngine |

---

## Connecting Accounts

### Facebook + Instagram
1. Tenant Admin clicks **Connect Facebook/Instagram** → Facebook Login for Business popup.
2. Callback exchanges the code for a user token, then a **long-lived** token.
3. API lists Pages (and linked Instagram Business accounts); the user selects which to connect to which **brand**.
4. A **Page access token** derived from the long-lived user token is stored per Page (Page tokens obtained this way do not expire unless revoked or the user changes password/permissions *(verify)*).
5. A `channelConnections` record is created per Page and per Instagram account.

### WhatsApp
1. Tenant Admin clicks **Connect WhatsApp** → Embedded Signup.
2. The tenant creates/selects their WhatsApp Business Account and registers a phone number.
3. Callback returns the WABA id and phone number id; the platform subscribes the app to the WABA's webhooks.
4. A `channelConnections` record is created (one per phone number).

The tenant's WhatsApp messaging is billed by Meta; the WABA payment method belongs to the tenant (decision — see Open Decisions).

### Disconnecting
- User-initiated: delete tokens, unsubscribe webhooks, cancel scheduled posts for that connection (status → `cancelled`, user notified).
- Meta-initiated (deauthorize webhook / token invalid): status `disconnected`, scheduled posts paused, Tenant Admin notified by email and in-app.

---

## Token Security
- Encrypted at rest with **ASP.NET Core Data Protection** (key ring persisted to Redis or a mounted volume, protected by a master key in Docker Secrets). Field stored as ciphertext in `channelConnections.encryptedToken`.
- Decrypted only inside `MetaGraphClient` at call time; never returned by any API, never logged, never in prompts.
- App secret in Docker Secrets; all Graph calls include `appsecret_proof`.
- `TokenHealthJob` (daily) validates each token (debug_token) and flags `expiring` / `invalid`.

---

## Publishing

### Channel adaptation (Social Agent)
Before scheduling, the Social Agent adapts an approved content item per channel. Deterministic validation then enforces:

| Rule | Facebook | Instagram |
|---|---|---|
| Caption length | ~63,000 chars *(verify)*; recommend ≤ 500 | ≤ 2,200 chars |
| Hashtags | Optional, ≤ 5 recommended | ≤ 30 |
| Links | Clickable | Not clickable in captions — use "link in bio" |
| Media required | No | Yes (image or video) |
| Image formats | JPEG, PNG | JPEG; aspect ratio 4:5 to 1.91:1 *(verify)* |
| Carousel | Post-MVP | 2–10 items |

If adaptation changes the text materially (not just hashtags/length trim), the adapted version returns to `pending_review`.

### Media hosting
Instagram (and Facebook URL-based uploads) fetch media **from a public URL**. MinIO is private, so `PublishPostJob` generates a **pre-signed MinIO URL** (expiry 1 hour) served through Nginx at `media.marketingstudio.ai`. Images are validated/resized to channel specs before upload.

### PublishPostJob
1. Load `contentItem`; abort unless status is `scheduled` and connection is `active`.
2. **Idempotency check:** if `platformPostId` already set → mark `published`, stop.
3. Acquire a Redis lock on `contentItemId` (prevents double execution).
4. Publish:
   - **Facebook:** `POST /{page-id}/photos` (image) or `/{page-id}/feed` (text/link).
   - **Instagram:** create media container `POST /{ig-user-id}/media` → poll container status until `FINISHED` (videos) → `POST /{ig-user-id}/media_publish`.
5. Store `platformPostId`, `permalink`, `publishedAt`; status `published`; emit `FacebookPostPublished` / `InstagramPostPublished`.
6. On failure: classify the error (below).

### Error handling
| Error type | Examples | Action |
|---|---|---|
| Transient | 5xx, timeout, rate limit | Retry: 1 min, 5 min, 15 min (Polly + Hangfire); then `failed` |
| Auth | Token invalid/expired, permission removed | No retry; connection → `disconnected`; post `failed`; notify Tenant Admin |
| Content | Media rejected, caption too long, policy violation | No retry; post `failed` with Meta's message; returns to editor |
| Limit | Instagram daily publish limit reached *(verify current limit per account)* | Reschedule to next available window; notify user |

Users see failure reasons in the portal and can **Retry** or **Edit & reschedule**.

### Scheduling
- Times are entered in the **brand's timezone** (new field `brands.timezone`) and stored in UTC.
- `suggest_post_time` uses the brand's past engagement by hour/day (from insights); falls back to channel defaults when history is thin.
- Cancel or reschedule is allowed until the job starts; it deletes/re-creates the Hangfire job.
- Max scheduling horizon: 90 days.
- Deleting a published post from the platform is post-MVP.

---

## WhatsApp Messaging (Support Agent)

### Inbound
1. Webhook delivers message → stored in `conversations` (by customer phone + phone number id).
2. Support Agent generates a reply (Agent Specification.md).
3. If brand has **auto-reply** enabled → send; otherwise queue for human review in the portal inbox.
4. Escalations appear in the portal inbox for a human agent to reply manually.

### WhatsApp rules
- Free-form replies are only allowed within the **24-hour customer service window** after the customer's last message.
- **Pricing (from 1 Oct 2026, verify):** each phone number gets 1,000 free service messages per month; replies beyond that are billed per message. Show the tenant their monthly message count in the inbox. See Infrastructure Costing.md → Third-Party Fees.
- Outside the window, only **pre-approved message templates** can be sent (templates submitted via the API and approved by Meta).
- Business-initiated marketing messages require customer **opt-in** and are post-MVP.
- Customer phone numbers are personal data: masked in logs, included in GDPR export/erasure.

### Conversation states
`open -> awaiting_human -> resolved` (auto-resolve after 24 h of inactivity).

---

## Webhooks
Endpoint: `POST /api/v1/webhooks/meta` (plus `GET` for Meta's verification handshake using a verify token from Docker Secrets).

1. Verify `X-Hub-Signature-256` (HMAC-SHA256 of raw body with app secret). Invalid → `401`, logged.
2. Return `200` within a few seconds; enqueue processing in Hangfire. Never call the LLM inline.
3. Deduplicate by event/message id (Redis set, 48 h TTL) — Meta may redeliver.
4. Route by object/field: WhatsApp messages & statuses, Page/permission changes, deauthorization.

Data deletion callback: `POST /api/v1/webhooks/meta/data-deletion` → enqueue deletion of the user's connection data, return a confirmation code + status URL as Meta requires.

---

## Insights (Analytics)
- `SyncInsightsJob` (Hangfire, hourly for posts < 7 days old, daily up to 30 days, then stop).
- Facebook: post reach, impressions, reactions, comments, shares, clicks. Instagram: reach, impressions, likes, comments, saves, shares. WhatsApp: sent, delivered, read, replied. *(verify metric names; several were renamed/deprecated in recent API versions)*
- Stored in the `analytics` collection per content item and channel; read by the Analytics Agent via `query_analytics`.

---

## Rate Limits
- Graph API uses app-level and Business Use Case (BUC) limits. Read the `X-App-Usage` and `X-Business-Use-Case-Usage` response headers; when usage > 80%, throttle that tenant's jobs (Redis token bucket per connection) and delay non-urgent insight syncs.
- Publishing jobs have priority over insight syncs.
- Batch insight requests where the API allows it.

---

## Data Model Additions
See MongoDB Collection Schemas.md: `channelConnections`, new fields on `contentItems`, `brands.timezone`, conversation fields for WhatsApp.

## API Endpoints
```text
GET    /api/v1/channels?brandId=
POST   /api/v1/channels/meta/connect          (returns login URL)
GET    /api/v1/channels/meta/callback
POST   /api/v1/channels/whatsapp/connect      (Embedded Signup completion)
DELETE /api/v1/channels/{id}
POST   /api/v1/content/{id}/schedule
POST   /api/v1/content/{id}/cancel
POST   /api/v1/content/{id}/retry
GET    /api/v1/inbox?brandId=                 (WhatsApp conversations needing a human)
POST   /api/v1/inbox/{conversationId}/reply
GET    /api/v1/webhooks/meta                  (verification)
POST   /api/v1/webhooks/meta
POST   /api/v1/webhooks/meta/data-deletion
```

Permissions: connecting/disconnecting channels — Tenant Admin only. Scheduling — roles with Content Approval "Approve". Inbox replies — Tenant Admin, Marketing Manager (see RBAC Matrix.md).

---

## Testing
- Unit: channel validation rules, error classification, idempotency, timezone conversion, webhook signature verification.
- Integration: `MetaGraphClient` against a **WireMock** stub of Graph API responses (success, rate limit, auth error, media rejected).
- Sandbox: Meta **test Pages/Instagram test accounts** and the WhatsApp **test phone number** in a staging app.
- E2E (staging): generate → approve → schedule → publish → insights appear.

## Observability
- Metrics: posts scheduled/published/failed by channel and error type, publish delay (scheduled vs actual), webhook latency and signature failures, connections by status, BUC usage %.
- Alerts: publish failure rate > 5% in 1 h, any connection-wide auth failures spike, webhook signature failures, BUC usage > 90%.

---

## Delivery Plan
| When | Work |
|---|---|
| Sprint 5 (latest) | Domain, one-page site with privacy/terms/data-deletion pages, Meta business portfolio + app, Business Verification |
| Sprint 7 | Submit App Review with screencasts once connect + publish work on staging; WhatsApp Tech Provider onboarding |
| Phase 6, week 1 | Connect flows, token encryption, `channelConnections`, webhook endpoint + verification |
| Phase 6, week 2 | Facebook & Instagram publishers, media pre-signed URLs, `PublishPostJob`, scheduling, Social Agent tools |
| Phase 6, week 3 | WhatsApp inbound → Support Agent, inbox, insights sync, error handling UI, E2E tests |

## Open Decisions
- **WhatsApp billing:** tenants pay Meta directly (own WABA payment method) vs platform pays and re-bills. Recommend tenant pays directly for MVP.
- **Next channel after Meta:** LinkedIn, X or TikTok (each needs its own app review/partner access).
- **Auto-reply default** for WhatsApp: off (human review) recommended until quality is proven.
- **Engagement on comments/DMs** for Facebook/Instagram: post-MVP scope and timing.
