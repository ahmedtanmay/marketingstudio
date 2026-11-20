# AI Integration Design

## Purpose
Defines how AI Marketing Studio talks to large language models (LLMs) and embedding models: provider abstraction, model selection, prompt management, safety, cost control, resilience and quality evaluation. Used by the agents defined in Agent Specification.md.

## Principles
- **Provider-agnostic.** Agents depend on an interface, not a vendor SDK. Switching or adding a provider is a configuration change.
- **Prompts are code.** Versioned, reviewed, tested before release.
- **Untrusted input stays data.** Knowledge-base documents, inbound customer messages and web content are never treated as instructions.
- **Every call is metered.** Tokens and cost are recorded per tenant, brand, agent and run.
- **Fail safe.** When the AI layer is degraded, users see a clear status; nothing is published without approval.

---

## Architecture

```text
AgentEngine
  -> IAgentRuntime (tool loop, guardrails)
     -> ILlmGateway                     (Application interface)
        -> LlmGateway                   (Infrastructure)
           ├── Prompt Registry          (versioned templates)
           ├── Model Router             (agent -> model tier -> provider/model)
           ├── Usage Meter              (tokens, cost, quota)
           ├── Resilience (Polly)       (retry, timeout, circuit breaker, fallback)
           └── Provider Adapters
               ├── OpenAI
               └── Secondary provider (fallback, e.g. Anthropic Claude)
     -> IEmbeddingService -> embedding provider -> Qdrant
```

**Implementation note (.NET 10):** build provider adapters on `Microsoft.Extensions.AI` (`IChatClient`, `IEmbeddingGenerator`), which has adapters for the major providers and supports middleware (logging, caching, telemetry). `ILlmGateway` wraps it so the rest of the code never references a vendor SDK.

### ILlmGateway (Application layer)
```csharp
public interface ILlmGateway
{
    Task<LlmResult> CompleteAsync(LlmRequest request, CancellationToken ct);
    IAsyncEnumerable<LlmStreamChunk> StreamAsync(LlmRequest request, CancellationToken ct);
}

public record LlmRequest(
    RunContext Context,          // tenantId, brandId, userId, runId, agentType
    string PromptId,             // e.g. "content.generate"
    IReadOnlyDictionary<string, object> Variables,
    IReadOnlyList<ToolDefinition> Tools,
    JsonSchema? OutputSchema,    // structured output when set
    ModelTier? TierOverride);
```

---

## Model Selection

Models are chosen by **tier**, not hard-coded. The tier-to-model mapping lives in configuration and is decided by benchmarking (see Evaluation).

| Tier | Use | Agents |
|---|---|---|
| **Reasoning** | Planning, multi-step tool use, campaign strategy | Planner, Campaign |
| **Standard** | Brand-voice copywriting, customer replies | Content, Support, Social |
| **Fast** | Classification, guardrail checks, routing hints, summaries | Guardrails, Analytics summaries |
| **Embedding** | Knowledge base vectors | Knowledge base ingestion & search |

### Configuration example
```json
"AI": {
  "Providers": {
    "OpenAI":   { "ApiKeySecret": "openai_api_key" },
    "Fallback": { "ApiKeySecret": "fallback_api_key" }
  },
  "Tiers": {
    "Reasoning": { "Primary": "OpenAI:<model>", "Fallback": "Fallback:<model>" },
    "Standard":  { "Primary": "OpenAI:<model>", "Fallback": "Fallback:<model>" },
    "Fast":      { "Primary": "OpenAI:<model>", "Fallback": "Fallback:<model>" }
  },
  "Embedding": { "Model": "OpenAI:text-embedding-3-small", "Dimensions": 1536 },
  "Limits": { "MaxOutputTokens": 4096, "RequestTimeoutSeconds": 60 }
}
```

Model names are filled in after the Phase 5 benchmark. If Anthropic is used as the secondary provider, candidates are `claude-sonnet-5-5` (Standard/Reasoning) and `claude-haiku-5-5` (Fast).

### Embeddings
- One embedding model for the whole platform; vector dimension is fixed per Qdrant collection.
- Changing the embedding model requires **re-indexing** all documents (background job, new collection, switch alias when complete).
- Embeddings are **not** used across providers interchangeably — chat fallback does not change the embedding model.

---

## Prompt Management

### Storage and versioning
- Templates are stored **in the Git repository** under `src/AgentEngine/Prompts/{agent}/{promptId}.v{n}.md` with front-matter metadata.
- Loaded into the Prompt Registry at startup; the active version per prompt is set in configuration.
- Every `agentRuns` record stores `promptId` and `promptVersion`, so any output can be traced to the exact prompt.
- Changes go through pull request review and must pass the evaluation suite before the active version is switched.
- Tenants **cannot** edit system prompts. They influence output only through the brand profile (tone, banned terms, audience) and knowledge base.

### Template front-matter
```yaml
id: content.generate
version: 3
tier: Standard
outputSchema: ContentItemBatch
maxOutputTokens: 2000
temperature: 0.7
```

### Prompt structure (every agent)
```text
[SYSTEM]
1. Platform rules        — role, safety rules, "never publish", tool-use rules
2. Brand context         — name, industry, tone of voice, audience, languages, banned words/topics
3. Agent instructions    — task-specific guidance, output format

[CONTEXT — untrusted data]
<knowledge>  retrieved knowledge-base chunks, each with source id  </knowledge>
<products>   product data  </products>
<history>    conversation history (Support Agent)  </history>

[USER]
The task / customer message
```

Retrieved content is wrapped in delimited blocks and the system prompt states that text inside them is reference material, not instructions.

---

## Structured Output and Tool Calling
- Agents that create records (Content, Campaign, Analytics) use **structured output** with a JSON schema. Responses failing schema validation are retried once with the validation error; then the run fails.
- Tool calling uses the provider's native function-calling. Tools are defined once in the Tool Registry (Agent Specification.md) and translated per provider by the adapter.
- Tool arguments from the model are validated against the tool's schema before execution. `tenantId`/`brandId` are injected from `RunContext` and are never accepted from the model.

---

## Context Window Management
| Component | Budget (share of input) |
|---|---|
| System + brand context | ≤ 15% |
| Retrieved knowledge | ≤ 50% (top-k chunks, k = 8 default, trimmed by relevance score) |
| Conversation history | ≤ 25% (older turns summarized by Fast tier) |
| Task | remainder |

Token counts are estimated before sending; if the budget is exceeded, knowledge chunks are dropped lowest-score first, then history is summarized.

---

## Safety and Guardrails

### Input side
- **Prompt injection:** knowledge-base documents and inbound messages are treated as untrusted (see Prompt structure). The Support Agent additionally runs a Fast-tier injection classifier on inbound messages.
- **Moderation:** inbound customer messages and user tasks pass the provider's moderation endpoint; flagged input is rejected or escalated.
- **PII:** customer phone numbers/emails in Support conversations are masked before logging.

### Output side
Run in order after generation (Agent Specification.md, lifecycle step 7):
1. Schema validation
2. Banned words/topics from the brand profile (deterministic check)
3. Channel rules (length, hashtag count, link rules)
4. Moderation endpoint
5. Brand-voice check (Fast tier scores tone against brand profile; below threshold → regenerate once)
6. Claim check for product facts (prices, features must appear in product data / knowledge base)

Failure handling: regenerate once with feedback; if still failing, save as `draft` with flags for human editing.

---

## Cost Control and Quotas

### Metering
The Usage Meter records per call: provider, model, tokens in/out, cached tokens, cost (from a price table in configuration), latency. Aggregated into `agentRuns` and a daily `usage` rollup per tenant.

### Plan quotas
Credit allowances, available agents and the credit definition are in **Billing and Subscriptions Design.md** (single source of truth for plan limits).

- Credits abstract over tokens so pricing is stable when models change (1 credit = fixed cost amount).
- Credits are **reserved before** the LLM call via `IEntitlementService`; at 80% the tenant admin is notified; at 100% runs are rejected with `QUOTA_EXCEEDED`.
- Platform-level daily spend cap with alerting, to protect against runaway loops.

### Cost reduction
- Use provider-side **prompt caching**: keep system prompt + brand context as a stable prefix.
- Cache embeddings of repeated queries in Redis (TTL 24 h).
- Route classification/guardrail work to the Fast tier.

---

## Resilience
| Concern | Policy (Polly) |
|---|---|
| Timeout | 60 s per call (configurable per tier) |
| Retry | 3 attempts, exponential backoff with jitter, on 429 / 5xx / timeout |
| Rate limits | Respect `Retry-After`; per-tenant concurrency limit (Redis) so one tenant cannot exhaust the provider quota |
| Circuit breaker | Open after 50% failures over 30 s; half-open after 60 s |
| Fallback | When the circuit is open, route the tier to its fallback model; fallback runs are tagged in `agentRuns` |
| Total outage | Runs queued in Hangfire; portal shows "AI temporarily unavailable" |

---

## Streaming
- Interactive chat (Planner, Content "generate" in the editor) streams tokens to the portal via **SignalR**.
- Guardrails run on the complete output; streamed text is shown as a draft until guardrails pass.
- Background runs (campaigns, scheduled reports) do not stream.

---

## Evaluation
Prompt or model changes are released only after passing evaluation.

- **Golden dataset** per agent: 30–50 representative tasks across several sample brands (stored in repo under `tests/AgentEval`).
- **Automatic checks:** schema validity, guardrail pass rate, length rules, banned-term leakage, tool-call correctness (expected tool sequence).
- **LLM-as-judge** (Reasoning tier) scores brand-voice fit, accuracy vs source data, and usefulness on a 1–5 rubric.
- **Release gate:** no regression > 5% on any metric vs the active version; guardrail pass rate ≥ 98%.
- **Production feedback:** approve/reject decisions and edit distance on approved content are tracked per prompt version as a live quality signal.
- Runs in CI on prompt changes (nightly for model comparisons).

---

## Data Privacy and Security
- Use provider API terms where customer data is **not used for training**; sign the provider's data processing agreement.
- API keys in Docker Secrets (Security Architecture.md); rotated every 90 days.
- Prompts and outputs are stored in `agentRuns` for 90 days (configurable per plan), then output is kept and input is purged.
- Tenant data is never mixed in a single LLM call; every retrieval is filtered by `tenantId` and `brandId`.
- Document which providers process data in the privacy policy (sub-processor list) for GDPR.

---

## Observability
- OpenTelemetry traces: agent run → LLM calls → tool calls, linked by `runId`.
- Serilog structured logs: `runId`, `tenantId`, `agentType`, `promptId`, `promptVersion`, model, tokens, cost, latency, outcome. Prompt/response bodies are **not** logged (stored only in `agentRuns`).
- Prometheus/Grafana dashboards: calls and cost per provider/model, p50/p95 latency, error and fallback rate, guardrail failure rate, quota rejections.
- Alerts: fallback active > 10 min, error rate > 5%, daily spend > threshold.

---

## Delivery Plan (within Phase 4–5)
| Step | Phase |
|---|---|
| Embedding service + Qdrant ingestion | Phase 4 |
| `ILlmGateway`, OpenAI adapter, Usage Meter, Polly policies | Phase 5, week 1 |
| Prompt Registry + first prompts (Planner, Content) | Phase 5, week 1 |
| Guardrail pipeline + structured output | Phase 5, week 2 |
| Evaluation harness + golden datasets; model benchmark to fill tier config | Phase 5, week 2–3 |
| Fallback provider adapter, streaming via SignalR | Phase 5, week 3 |

## Open Decisions
- Secondary/fallback provider: Anthropic Claude or another vendor.
- Final model per tier (after benchmark).
- Image generation in MVP: provider, cost per image, and moderation of generated images.
- AI credit price and quota per plan (Billing design).
- Data residency requirements (e.g. EU-only processing) for target customers.
