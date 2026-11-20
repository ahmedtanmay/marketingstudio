# UI Screen Flows and Wireframes

## Purpose
Low-fidelity structure of the Angular application portal: navigation, screen inventory, key user flows, wireframes of the core screens, and the UI states every screen must handle. It is the brief for the UI/UX designer, who produces the high-fidelity designs and design system. Behaviour and acceptance criteria are in User Stories and Acceptance Criteria.md.

## Design Principles
- **Brand context is always visible.** The active brand is shown in the header; every screen shows only that brand's data.
- **Review before publish is the centre of the product.** Generated content always lands in a review step that is one click away from anywhere.
- **Show AI provenance.** Generated items show which agent produced them, the knowledge sources used, guardrail flags and credits spent.
- **Plan limits are explained, not hidden.** When a limit blocks an action, the UI says which limit and offers the upgrade or top-up path.
- **Role-aware UI.** Actions a role cannot perform are hidden (or disabled with a tooltip where discoverability matters, e.g. Approve for Content Manager).
- Angular Material components; WCAG 2.1 AA; usable from 1280 px desktop, review queue and inbox usable on tablet.

---

## Navigation

```text
┌───────────────────────────────────────────────────────────────────────────┐
│ [Logo]  [Brand: Acme Coffee ▾]          [Credits 1,240 ▮▮▮▯] [🔔] [User ▾]│
├──────────────┬────────────────────────────────────────────────────────────┤
│ Dashboard    │                                                            │
│ Agent Chat   │                                                            │
│ Content      │                     Page content                           │
│   Review (5) │                                                            │
│   Calendar   │                                                            │
│   Library    │                                                            │
│ Campaigns    │                                                            │
│ Inbox (2)    │                                                            │
│ Analytics    │                                                            │
│ ──────────── │                                                            │
│ Brand        │                                                            │
│   Profile    │                                                            │
│   Products   │                                                            │
│   Knowledge  │                                                            │
│   Media      │                                                            │
│ ──────────── │                                                            │
│ Settings     │                                                            │
│   Users      │                                                            │
│   Channels   │                                                            │
│   Billing    │                                                            │
│   Workspace  │                                                            │
└──────────────┴────────────────────────────────────────────────────────────┘
```
- Badges show pending review items and conversations awaiting a human.
- Credits meter turns amber at 80% and red at 100%; clicking opens Billing → Credits.
- Global banners (above page content): trial days left, past due, read-only/suspended, email not verified.
- Super Admin has a separate **Admin** area (tenants, credits, entitlements) not shown to tenant users.

## Screen Inventory
| # | Screen | Route | Roles | Stories |
|---|---|---|---|---|
| 1 | Sign in / Sign up / Reset / Accept invite | `/login`, `/signup`, `/reset`, `/invite/:token` | Public | AUTH-1–5 |
| 2 | Dashboard (+ onboarding checklist) | `/dashboard` | All | TEN-1, ANL-1 |
| 3 | Agent Chat | `/agents/chat` | Execute roles | AGT-1 |
| 4 | Generate Content (dialog) | from Chat / Content / Campaign | Execute roles | AGT-2 |
| 5 | Review Queue | `/content/review` | All (actions by role) | CNT-1–3 |
| 6 | Content Editor | `/content/:id` | All (actions by role) | CNT-1, CNT-2, SOC-3 |
| 7 | Content Calendar | `/content/calendar` | All | CNT-4, SOC-3–5 |
| 8 | Content Library | `/content` | All | — |
| 9 | Campaigns list / Campaign detail | `/campaigns`, `/campaigns/:id` | All | CMP-1, CMP-2 |
| 10 | Inbox | `/inbox` | Admin, Mkt Manager (reply); Content Mgr (read) | INB-1, INB-2 |
| 11 | Analytics | `/analytics` | All | ANL-1, ANL-2 |
| 12 | Brand Profile & Voice | `/brand/profile` | Edit: Admin, Mkt Mgr | BRD-1, BRD-2 |
| 13 | Products | `/brand/products` | Edit: Admin, Mkt Mgr | PRD-1 |
| 14 | Knowledge Base (documents, FAQs, test search) | `/brand/knowledge` | Edit: Admin, Mkt Mgr, Content Mgr | KB-1–4 |
| 15 | Media Library | `/brand/media` | Edit: Admin, Mkt Mgr, Content Mgr | BRD-3 |
| 16 | Brands list / Create brand | `/brands`, `/brands/new` | Admin, Mkt Mgr | BRD-1, BRD-5 |
| 17 | Users & Invites | `/settings/users` | Admin | AUTH-5, AUTH-6 |
| 18 | Channels | `/settings/channels` | Admin | SOC-1, SOC-2, SOC-6 |
| 19 | Billing (plan, credits, usage, invoices) | `/settings/billing` | Admin (usage: Mkt Mgr) | BIL-1–6 |
| 20 | Agent Run History | `/agents/runs` | Admin, Mkt Mgr | AGT-4 |
| 21 | Workspace settings | `/settings/workspace` | Admin | TEN-2 |
| 22 | Admin: tenants, credits, entitlements | `/admin/...` | Super Admin | ADM-1–3 |

---

## Key Flows

### F1 — First-run onboarding
```text
Website "Start free trial" → Sign up → Verify email (can continue) → Create first brand
→ Dashboard with checklist:
   [✓] Create brand  [ ] Add products  [ ] Upload knowledge  [ ] Connect a channel  [ ] Generate first post
→ each item deep-links to its screen; checklist hides when all done (can be reopened)
```

### F2 — Generate → review → approve → schedule → publish (core loop)
```text
Agent Chat or "Generate" button
 → Generate dialog (channel, type, count, products, language) → shows estimated credits
 → progress (streamed) → N items created as "Pending review"
 → toast "3 posts ready for review" → Review Queue
 → open item in Content Editor → edit / regenerate with feedback / reject (reason) / approve
 → Approved → "Schedule" → pick channel(s) + time (brand timezone, suggested time) → Scheduled
 → Calendar shows item → at time: Published (link to live post) or Failed (reason + Retry / Edit & reschedule)
```

### F3 — Campaign
```text
Campaigns → New campaign (goal, products, channels, dates) → Campaign Agent drafts plan
 → Campaign detail: timeline of slots (date, channel, brief) — edit, add, remove slots
 → select slots → "Generate content" → items appear in Review Queue tagged with the campaign
 → approved items scheduled per slot → campaign progress (scheduled / published / failed)
```

### F4 — Knowledge base
```text
Knowledge → Upload (drag & drop) → row appears "Processing…" with live progress
 → Ready (chunk count) | Failed (reason, Retry) | Rejected (virus/duplicate)
 → "Test search" tab: ask a question → see top results with source + score
```

### F5 — Connect channels
```text
Settings → Channels → "Connect Facebook & Instagram" → Meta popup → choose Pages/IG accounts
 → assign each to a brand → Active
"Connect WhatsApp" → Embedded Signup → number appears → assign brand → Active
Disconnected channel → red status + "Reconnect"; affected scheduled posts listed
```

### F6 — WhatsApp inbox
```text
Inbox (badge) → conversation list (Awaiting human first) → conversation
 → AI draft shown above composer → Send / Edit & send / Discard / Write own
 → outside 24 h window: composer disabled → "Send template" picker
 → Resolve
```

### F7 — Limits and billing
```text
Any blocked action (402) → dialog: "You've used 3 of 3 channels on Starter."
 → [Upgrade plan] → Billing → plan comparison → Stripe Checkout → back → limits updated
Credits exhausted → dialog with balance → [Buy credits] → Checkout → back → retry action
```

---

## Wireframes

### W1 — Dashboard
```text
┌ Dashboard ─────────────────────────────────────── [Last 30 days ▾] ┐
│ ┌ Getting started (3/5) ──────────────────────────────────── [×] ┐ │
│ │ ✓ Create brand  ✓ Add products  ✓ Upload knowledge             │ │
│ │ ○ Connect a channel →   ○ Generate your first post →           │ │
│ └────────────────────────────────────────────────────────────────┘ │
│ ┌ Pending review ┐ ┌ Scheduled ┐ ┌ Published ┐ ┌ Credits used    ┐ │
│ │      5   →     │ │    12     │ │    48     │ │ 1,240 / 3,000   │ │
│ └────────────────┘ └───────────┘ └───────────┘ └─────────────────┘ │
│ ┌ Engagement by channel (line chart) ─┐ ┌ Upcoming posts ─────────┐ │
│ │                                     │ │ Tue 10:00 IG  Summer…   │ │
│ │                                     │ │ Wed 18:00 FB  New blend │ │
│ └─────────────────────────────────────┘ └─────────────────────────┘ │
│ ┌ Campaign performance ───────────────┐ ┌ Agent activity ─────────┐ │
│ │ Summer Sale   reach 12.4k  ▲ 18%    │ │ Content Agent  3 posts  │ │
│ └─────────────────────────────────────┘ └─────────────────────────┘ │
└────────────────────────────────────────────────────────────────────┘
```

### W2 — Agent Chat
```text
┌ Agent Chat ─────────────────────────────────────── [New chat] ┐
│ History        │                                              │
│ ─────────────  │  You: Write 3 Instagram posts for the        │
│ Summer posts   │       summer cold-brew launch                │
│ Q3 campaign    │                                              │
│ Last month…    │  Planner → Content Agent                     │
│                │  ┌──────────────────────────────────────┐    │
│                │  │ ✓ 3 posts created · 4 credits         │    │
│                │  │ [Review now →]                        │    │
│                │  └──────────────────────────────────────┘    │
│                │                                              │
│                │ ┌──────────────────────────────────────────┐ │
│                │ │ Ask the agents…                    [Send]│ │
│                │ └──────────────────────────────────────────┘ │
│                │  Suggestions: [Plan a campaign] [How did …] │
└────────────────┴──────────────────────────────────────────────┘
```

### W3 — Review Queue
```text
┌ Review (5) ─────────── [Channel ▾] [Campaign ▾] [Flags only ☐] ┐
│ ☐ │ Preview                         │ Channel │ Source    │ Flags│
│ ☐ │ "Beat the heat with our new…"   │ IG      │ Content   │      │
│ ☐ │ "Cold brew, done right. ☕…"    │ IG      │ Content   │ ⚠ 1 │
│ ☐ │ "Our summer blend is here…"     │ FB      │ Campaign  │      │
│ ─────────────────────────────────────────────────────────────── │
│ Selected: 2   [Approve] [Reject…]          (Approve hidden/     │
│                                             disabled for        │
│                                             Content Manager)    │
└─────────────────────────────────────────────────────────────────┘
```

### W4 — Content Editor
```text
┌ Content · Pending review ────────────────────── [Reject] [Approve] ┐
│ ┌ Editor ───────────────────────────┐ ┌ Preview (Instagram) ─────┐ │
│ │ Caption                    842/2200│ │ ┌──────────────────────┐ │ │
│ │ ┌───────────────────────────────┐ │ │ │      [image]         │ │ │
│ │ │ Beat the heat with our new…   │ │ │ └──────────────────────┘ │ │
│ │ └───────────────────────────────┘ │ │ acmecoffee  Beat the… │ │
│ │ Hashtags  #coldbrew #summer (2/30)│ └──────────────────────────┘ │
│ │ Media   [image ×] [+ From library]│                               │
│ │ CTA     Link in bio               │ ┌ AI details ──────────────┐ │
│ ├───────────────────────────────────┤ │ Agent: Content · v3      │ │
│ │ ⚠ Guardrail: unsupported claim    │ │ Credits: 1               │ │
│ │   "best in the city" [Dismiss]    │ │ Sources: Product sheet,  │ │
│ ├───────────────────────────────────┤ │  FAQ: "Cold brew…"       │ │
│ │ Regenerate with feedback:         │ └──────────────────────────┘ │
│ │ [ make it shorter, more playful ] │ ┌ History ─────────────────┐ │
│ │                    [Regenerate]   │ │ Created by Content Agent │ │
│ └───────────────────────────────────┘ └──────────────────────────┘ │
│ After approval: [Schedule…]                                        │
└────────────────────────────────────────────────────────────────────┘
```

### W5 — Schedule dialog
```text
┌ Schedule post ─────────────────────────────┐
│ Channels  ☑ Instagram @acmecoffee           │
│           ☐ Facebook  Acme Coffee           │
│ Date/time [ 2026-12-02 ] [ 10:00 ]          │
│ Timezone  Asia/Kolkata (brand)              │
│ 💡 Suggested: Tue 10:00 (best engagement)   │
│ Scheduled this month: 64 / 100              │
│                       [Cancel] [Schedule]   │
└─────────────────────────────────────────────┘
```

### W6 — Content Calendar
```text
┌ Calendar ── [◀ Dec 2026 ▶] [Month|Week] [Channel ▾] [Campaign ▾] ┐
│ Mon     Tue          Wed        Thu        Fri      Sat     Sun   │
│  1      2            3          4          5        6       7     │
│         ▣ IG 10:00   ▣ FB 18:00            ✕ IG     ▣ IG          │
│         Summer…      New blend             Failed   Weekend…      │
│ ...                                                               │
│ Legend: ▣ scheduled  ✓ published  ✕ failed   (drag to reschedule) │
└───────────────────────────────────────────────────────────────────┘
```

### W7 — Knowledge Base
```text
┌ Knowledge ──────────── [Documents] [FAQs] [Test search] ──────────┐
│ ┌ Drop files here or [Browse] — PDF, DOCX, TXT, MD, HTML · ≤ 20 MB┐│
│ └─────────────────────────────────────────────────────────────────┘│
│ Usage: 18 / 200 documents · 120 MB / 5 GB                          │
│ Name                  Status            Chunks  Uploaded   Actions │
│ Brand guidelines.pdf  ● Ready             64    Nov 12     ⋮       │
│ Price list.docx       ◐ Processing 60%     –    Today      ⋮       │
│ Scan.pdf              ✕ Failed: no text    –    Today      Retry ⋮ │
└────────────────────────────────────────────────────────────────────┘
```

### W8 — Inbox
```text
┌ Inbox ──────────────────────────────────────────── [Auto-reply: Off] ┐
│ Awaiting human (2)   │ +91 98•••• 210 · WhatsApp · window: 18h left  │
│ ● +91 98•••• 210     │ ───────────────────────────────────────────── │
│ ● +44 77•••• 931     │ Customer: Do you deliver on Sundays?          │
│ Open (5)             │ ┌ AI draft (confidence: high) ──────────────┐ │
│ ○ +1 415•••• 220     │ │ Yes! We deliver every day 8am–8pm…        │ │
│ Resolved             │ │ Source: FAQ "Delivery times"              │ │
│                      │ │ [Send] [Edit & send] [Discard]            │ │
│                      │ └───────────────────────────────────────────┘ │
│                      │ [ Type a reply…                     ] [Send]  │
│                      │ Free service messages this month: 412 / 1,000 │
│                      │                               [Mark resolved] │
└──────────────────────┴───────────────────────────────────────────────┘
```

### W9 — Brand Profile & Voice
```text
┌ Brand profile · Acme Coffee ───────────────────────────── [Save] ┐
│ Name [Acme Coffee]  Industry [Food & Beverage ▾]                  │
│ Languages [English ×] [Hindi ×] [+]   Timezone [Asia/Kolkata ▾]   │
│ Target audience [ Urban professionals 25–40 … ]                   │
│ Tone  (•) Friendly ( ) Professional ( ) Playful ( ) Custom        │
│ Voice guidelines [ Warm, short sentences, no slang …   ] 312/2000 │
│ Example posts  [1 …] [2 …] [+ Add]                                │
│ Banned words/topics [cheap ×] [politics ×] [+]                    │
│ [Preview voice] → sample post shown in side panel (no credits     │
│                   charged for drafts? → see Open Points)          │
└───────────────────────────────────────────────────────────────────┘
```

### W10 — Channels
```text
┌ Channels ──────────────────────── 3 / 15 used ────────────────────┐
│ [Connect Facebook & Instagram]  [Connect WhatsApp]                │
│ Platform   Account            Brand        Status        Actions  │
│ Facebook   Acme Coffee        Acme Coffee  ● Active      ⋮        │
│ Instagram  @acmecoffee        Acme Coffee  ● Active      ⋮        │
│ WhatsApp   +91 80•••• 555     Acme Coffee  ● Disconnected Reconnect│
└───────────────────────────────────────────────────────────────────┘
```

### W11 — Billing
```text
┌ Billing ─────────────── [Plan] [Credits & usage] [Invoices] ─────┐
│ Growth · Monthly · Active · renews 2026-12-18                     │
│ [Change plan]  [Manage payment method ↗]                          │
│ Limits: Brands 2/5 · Users 6/15 · Channels 3/15 · Posts 64/1,000  │
│ ┌ Credits ───────────────────────────────────────────────────────┐ │
│ │ 1,240 of 3,000 used this period   ▮▮▮▮▯▯▯▯▯▯   Top-ups: 500   │ │
│ │ [Buy credits]                                                  │ │
│ │ By agent: Support 62% · Content 21% · Planner 10% · Other 7%   │ │
│ └────────────────────────────────────────────────────────────────┘ │
└───────────────────────────────────────────────────────────────────┘
```

---

## UI States (every screen)
| State | Behaviour |
|---|---|
| Loading | Skeleton placeholders (no full-page spinners after first load) |
| Empty | Short explanation + primary action (e.g. "No documents yet — upload brand guidelines so agents write accurately. [Upload]") |
| Error | Inline message with retry; correlation id shown for support |
| No permission | Page hidden from navigation; direct URL shows "You don't have access" |
| Read-only tenant | Banner + all write actions disabled with tooltip linking to Billing |
| Limit reached | Action button opens the limit dialog (F7) |
| Long-running (AI, ingestion) | Live progress via SignalR; user can leave the page and gets a notification on completion |
| AI unavailable | Banner "AI temporarily unavailable — requests are queued" |

## Notifications
In-app (bell) and email, per user preferences: content ready for review, item approved/rejected, publish failed, channel disconnected, conversation awaiting human, credits at 80%/100%, trial ending, payment failed.

## Hand-off to Design
The designer should deliver:
1. Design system on Angular Material: colour tokens (with tenant theming slots — TEN-2), typography, spacing, light and dark mode.
2. High-fidelity designs for W1–W11 and the auth screens, including empty/error/loading states.
3. Clickable prototype of flows F1 and F2 for usability testing with 3–5 target users before Sprint 5.
4. Channel post previews (Instagram, Facebook, WhatsApp) that match the real platform rendering closely enough for approval decisions.

## Open Points
- Whether "Preview voice" and regenerations consume credits (recommend: yes, but show the cost before running).
- Mobile app or responsive-only for approvals on the go (MVP: responsive review queue).
- Dark mode in MVP or later.
