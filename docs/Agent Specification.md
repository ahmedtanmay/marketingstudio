# Agent Specification

## Purpose
Defines each AI agent in AI Marketing Studio: what it does, what it receives, which tools it may call, what it returns, and the guardrails around it. This is the single agent document (it replaces the earlier Agent Framework Design and Agent Orchestration Architecture outlines, now in `_archive`).

## Agents and Orchestration
```text
User request ─▶ Planner Agent ─▶ Specialist agent(s) ─▶ Tools ─▶ Result (drafts / plans / replies)
                                  Content · Campaign · Social · Support · Analytics
```
Specialist agents can also be called directly from the portal (e.g. a "Generate" button), bypassing the Planner.

## Scope and Principles
- Every agent run is scoped to exactly one **tenant** and one **brand**.
- Agents **never publish directly**. Anything that reaches a public channel goes through the Content Approval workflow.
- Agents only call tools from the Tool Registry they are granted. No free-form HTTP or database access.
- Every run is recorded in the `agentRuns` collection (input, output, tokens, cost, status).
- Agents are stateless between runs; context comes from the brand profile, knowledge base and conversation history.

---

## Common Agent Contract

### Request
```json
{
  "tenantId": "string (from JWT, never from the body)",
  "brandId": "string",
  "userId": "string",
  "agentType": "planner | content | campaign | social | support | analytics",
  "task": "natural-language instruction",
  "parameters": { "agent-specific": "see each agent" },
  "conversationId": "string | null"
}
```

### Response
```json
{
  "runId": "string",
  "status": "completed | failed | needs_input | pending_review",
  "output": { "agent-specific": "see each agent" },
  "contentItemIds": ["created contentItems, if any"],
  "usage": { "tokensIn": 0, "tokensOut": 0, "costUsd": 0.0 },
  "error": { "code": "string", "message": "string" }
}
```

Runs longer than 10 seconds are executed as Hangfire background jobs; the API returns `202 Accepted` with `runId`, and the portal receives progress via SignalR and can poll `GET /api/v1/agents/runs/{runId}`.

### Execution Lifecycle
1. Validate request (FluentValidation) and authorize (RBAC: role must have `Execute` on Agents).
2. Check tenant usage quota (plan limits).
3. Load **brand context**: name, industry, tone of voice, languages, banned words/topics, target audience.
4. Retrieve **knowledge** from Qdrant (top-k chunks, filtered by `tenantId` + `brandId`).
5. Build prompt from the versioned prompt template for the agent.
6. Call the LLM; execute tool calls in a loop (max 8 tool calls per run).
7. Run **output guardrails** (brand-voice check, banned terms, PII, platform length limits).
8. Persist results (`agentRuns`, `contentItems`) and emit domain events.
9. Return response.

---

## Planner Agent

| | |
|---|---|
| **Purpose** | Interpret a user request and route it to one or more specialist agents. |
| **Triggered by** | Any free-form request from the portal's agent chat. |
| **Inputs** | `task`, brand context, list of available agents for the tenant's plan. |
| **Tools** | `route_to_agent`, `ask_user_clarification` |
| **Output** | Execution plan: ordered list of `{ agentType, task, parameters }`; aggregated results of sub-runs. |
| **Guardrails** | Max 5 sub-agent calls per request. Cannot call itself. Only routes to agents enabled for the tenant's plan. |

**Routing examples**
- "Write 3 Instagram posts for our summer sale" → Content Agent (channel: instagram, count: 3)
- "Plan a 4-week launch for product X and schedule posts" → Campaign Agent → Content Agent → Social Agent
- "How did last month's campaign do?" → Analytics Agent

Direct calls to a specialist agent (e.g. from a "Generate post" button) bypass the Planner.

---

## Content Agent

| | |
|---|---|
| **Purpose** | Generate on-brand marketing copy and image prompts. |
| **Triggered by** | Planner, Campaign Agent, or directly from the portal. |
| **Parameters** | `channel` (instagram, facebook, whatsapp, email, blog), `contentType` (post, caption, ad, email, article), `count` (1–10), `productIds`, `language`, `campaignId` (optional) |
| **Tools** | `get_brand_profile`, `search_knowledge_base`, `get_products`, `create_content_item` |
| **Output** | `contentItems` in status `pending_review`, each with body, hashtags, call-to-action, suggested media prompt. |
| **Guardrails** | Channel length limits (e.g. Instagram caption ≤ 2,200 chars); no claims about products that aren't in product data or knowledge base; banned words/topics from brand profile; language must be one of the brand's `languages`. |

---

## Campaign Agent

| | |
|---|---|
| **Purpose** | Plan multi-step marketing campaigns. |
| **Triggered by** | Planner or the Campaigns module. |
| **Parameters** | `goal` (awareness, leads, sales, launch), `productIds`, `channels`, `startDate`, `endDate`, `budget` (optional) |
| **Tools** | `get_brand_profile`, `get_products`, `search_knowledge_base`, `get_campaign_history`, `create_campaign`, `invoke_content_agent` |
| **Output** | A `campaigns` record in status `draft` with a schedule (date, channel, content brief per slot); optionally generated content via Content Agent. |
| **Guardrails** | Max campaign length 90 days; max 60 scheduled slots; does not schedule anything — scheduling happens only after approval. |

---

## Social Agent

| | |
|---|---|
| **Purpose** | Adapt approved content for each social platform and schedule/publish it. |
| **Triggered by** | Approval of a `contentItem`, or Planner. |
| **Parameters** | `contentItemIds`, `channels`, `scheduledAt` (optional; else best-time suggestion) |
| **Tools** | `get_content_item`, `get_channel_connection`, `suggest_post_time`, `schedule_post` |
| **Output** | Updated `contentItems` with status `scheduled`; Hangfire job per post. |
| **Guardrails** | Only acts on items in status `approved`; respects platform rate limits; fails safely if the channel's OAuth token is expired (status `failed`, user notified). |

Publishing itself is done by the scheduler job, not by the LLM.

---

## Support Agent

| | |
|---|---|
| **Purpose** | Answer customer questions on behalf of the brand (e.g. WhatsApp, website chat). |
| **Triggered by** | Inbound message from a connected channel. |
| **Parameters** | `conversationId`, `message`, `channel` |
| **Tools** | `search_knowledge_base`, `get_products`, `get_conversation_history`, `escalate_to_human` |
| **Output** | Reply message; or escalation. Stored in `conversations`. |
| **Guardrails** | Answers only from the knowledge base and product data; if retrieval confidence is low, says it doesn't know and escalates. Never makes promises about refunds, pricing changes or legal matters — always escalates these. Replies are sent automatically only if the brand has enabled auto-reply; otherwise they queue for review. |

---

## Analytics Agent

| | |
|---|---|
| **Purpose** | Explain performance data and recommend next actions. |
| **Triggered by** | Planner, Analytics dashboard, or weekly scheduled report. |
| **Parameters** | `campaignId` or `dateRange`, `channels`, `metrics` |
| **Tools** | `query_analytics` (read-only, tenant-filtered), `get_campaign`, `get_content_items` |
| **Output** | Summary text, key metrics table, up to 5 recommendations. |
| **Guardrails** | Read-only. Every number in the summary must come from a `query_analytics` result — no estimated figures. |

---

## Tool Registry

| Tool | Read/Write | Used by |
|---|---|---|
| `get_brand_profile` | R | All |
| `search_knowledge_base` | R | Content, Campaign, Support |
| `get_products` | R | Content, Campaign, Support |
| `get_campaign_history` / `get_campaign` | R | Campaign, Analytics |
| `get_content_item(s)` | R | Social, Analytics |
| `get_conversation_history` | R | Support |
| `get_channel_connection` | R | Social |
| `query_analytics` | R | Analytics |
| `suggest_post_time` | R | Social |
| `create_content_item` | W | Content |
| `create_campaign` | W | Campaign |
| `schedule_post` | W | Social |
| `escalate_to_human` | W | Support |
| `route_to_agent` / `invoke_content_agent` | — | Planner, Campaign |
| `ask_user_clarification` | — | Planner |

Every tool receives `tenantId` and `brandId` from the run context, not from the LLM, so a model cannot request another tenant's data.

---

## Content Approval Workflow

```text
draft -> pending_review -> approved -> scheduled -> published
                       \-> rejected (with reason; can be regenerated)
                                     scheduled -> failed (retry / notify)
```

- Content Manager can submit; Marketing Manager / Tenant Admin can approve (see RBAC Matrix.md).
- Tenants may enable **auto-approve** per channel (off by default).
- All transitions are audit-logged.

---

## Error Handling
| Situation | Behaviour |
|---|---|
| LLM timeout / 5xx | Retry with exponential backoff (Polly), max 3 attempts, then `failed` |
| LLM rate limit | Queue and retry; surface "delayed" status to user |
| Tool error | Returned to the model as a tool result; after 2 failures of the same tool the run fails |
| Guardrail violation | Regenerate once with violation feedback; if still violating, save as `draft` flagged for human edit |
| Quota exceeded | Reject before LLM call with `QUOTA_EXCEEDED` |

## Limits (defaults, configurable per plan)
- Max tool calls per run: 8
- Max sub-agents per Planner request: 5
- Max run duration: 120 s
- Max content items per request: 10

## Observability
- Each run logs `runId`, `tenantId`, `agentType`, model, prompt template version, tokens, cost, latency (Serilog).
- Metrics in Prometheus: runs per agent, failure rate, p95 latency, cost per tenant.

## Open Decisions
Model selection, prompt versioning and evaluation are defined in AI Integration Design.md. Remaining:
- Image generation: in scope for MVP or prompts only?
