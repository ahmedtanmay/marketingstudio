# Knowledge Base (RAG) Design

## Purpose
Defines how brand knowledge (documents, product data, FAQs) is ingested, stored, searched and supplied to agents through retrieval-augmented generation (RAG). Delivered in Phase 4; used by the Content, Campaign and Support agents (Agent Specification.md) through the `search_knowledge_base` tool.

## Principles
- **Tenant and brand isolation is enforced by the platform, not by the model.** Every write and every search carries `tenantId` + `brandId` from the run context.
- **Retrieved text is untrusted data** (see AI Integration Design.md → Prompt structure).
- **Answers are grounded and traceable.** Every chunk carries its source, so outputs can cite where a fact came from.
- **Ingestion is asynchronous.** Uploads return immediately; processing happens in background jobs.

---

## What Goes Into the Knowledge Base

| Source | How it arrives | Embedded? | Notes |
|---|---|---|---|
| Uploaded documents (PDF, DOCX, TXT, MD, HTML) | Portal upload → MinIO | Yes | Brand guidelines, product sheets, FAQs, policies |
| Product records | `products` collection | Yes | Re-embedded on create/update via `ProductAddedEvent` / `ProductUpdatedEvent` |
| FAQ entries (Q&A pairs typed in the portal) | Portal form | Yes | Each Q&A = one chunk; high value for Support Agent |
| Brand profile (tone, audience, banned terms) | `brands` collection | **No** | Small and always needed — injected directly into the system prompt |
| Website pages (crawl) | — | Out of MVP | See Open Decisions |
| Images / scanned PDFs (OCR) | — | Out of MVP | Image files are stored as media assets only |

---

## Architecture

```text
Portal upload
  -> API: POST /api/v1/knowledge/documents
     -> MinIO (raw file, key: {tenantId}/{brandId}/kb/{documentId}/{fileName})
     -> MongoDB documents (status: uploaded)
     -> Hangfire job: IngestDocumentJob
          1. Validate & scan
          2. Extract text
          3. Clean & normalize
          4. Chunk
          5. Embed (IEmbeddingService, batched)
          6. Upsert to Qdrant
          7. Update documents (status: ready, chunkCount)

Agent run
  -> search_knowledge_base(query)
     -> IKnowledgeRetriever
        -> embed query (Redis cache)
        -> Qdrant search (filter: tenantId + brandId [+ sourceType])
        -> threshold, dedupe, (optional rerank)
        -> chunks with source references
```

### Components (Clean Architecture placement)
| Component | Layer |
|---|---|
| `IKnowledgeRetriever`, `IDocumentIngestionService`, `IEmbeddingService` | Application (interfaces) |
| Upload/Delete commands, Search query (MediatR) | Application |
| `QdrantKnowledgeStore`, `MinioFileStore`, text extractors, `IngestDocumentJob` | Infrastructure |
| `search_knowledge_base` tool | AgentEngine |

---

## Ingestion Pipeline

### 1. Validate and scan
- Allowed types: `.pdf`, `.docx`, `.txt`, `.md`, `.html`. Content type is checked from the file bytes, not the extension.
- Max file size: 20 MB (configurable per plan).
- Virus scan with ClamAV (container in Docker Compose). Infected files → status `rejected`, file deleted from MinIO.
- SHA-256 checksum; identical file already in the same brand → reject as duplicate.

### 2. Extract text
| Format | Library (.NET) |
|---|---|
| PDF | UglyToad.PdfPig |
| DOCX | DocumentFormat.OpenXml |
| HTML | HtmlAgilityPack (strip scripts, nav, footer) |
| TXT / MD | Direct read (UTF-8) |

Heading structure is preserved (as Markdown headings) because chunking uses it. A PDF that yields almost no text (likely scanned) → status `failed` with reason "no extractable text (OCR not supported)".

### 3. Clean and normalize
- Normalize whitespace and Unicode; remove repeated headers/footers and page numbers.
- Detect language (stored as chunk metadata).
- Optional PII masking for documents flagged "customer data" (emails, phone numbers).

### 4. Chunk
- **Structure-aware:** split on headings first, then paragraphs, then sentences.
- **Target size:** ~500 tokens, max 800; **overlap** ~75 tokens between consecutive chunks.
- Each chunk is prefixed with its heading path (e.g. `Returns Policy > International orders`) so it makes sense on its own.
- Product records: one chunk per product (name, category, price, description, key features).
- FAQ entries: one chunk per Q&A pair.

### 5. Embed
- Embedding model and dimensions from configuration (AI Integration Design.md → Embeddings).
- Batches of up to 100 chunks; retries via the shared Polly policies.
- Embedding token usage is metered and counts toward the tenant's AI credits.

### 6. Upsert to Qdrant
Point ID = deterministic UUID from `documentId + chunkIndex`, so re-ingestion overwrites rather than duplicates.

### Document status lifecycle
```text
uploaded -> processing -> ready
                      \-> failed (reason stored; user can retry)
uploaded -> rejected (virus / duplicate / invalid type)
ready -> deleting -> (removed)
```
Progress and status changes are pushed to the portal via SignalR.

---

## Qdrant Design

### Collection strategy
**One shared collection** (`knowledge_v1`) for all tenants, partitioned by payload — consistent with the shared-database multi-tenant model (Multi-Tenant Design.md).

- Payload index on `tenantId` configured as the tenant key (`is_tenant: true`), so Qdrant co-locates each tenant's vectors and filtered searches stay fast.
- Payload indexes on `brandId`, `sourceType`, `documentId`, `language`.
- Collection name is accessed through an alias (`knowledge`) so a re-index can build `knowledge_v2` and switch the alias with no downtime.

Very large enterprise tenants can later be moved to a dedicated collection without changing the retriever interface.

### Point payload
```json
{
  "tenantId": "t_123",
  "brandId": "b_456",
  "sourceType": "document | product | faq",
  "sourceId": "documentId / productId / faqId",
  "documentId": "d_789",
  "title": "Returns Policy",
  "headingPath": "Returns Policy > International orders",
  "chunkIndex": 4,
  "language": "en",
  "text": "chunk text",
  "createdAt": "2026-11-20T10:00:00Z"
}
```

### Isolation enforcement
- `QdrantKnowledgeStore` is the only class that talks to Qdrant. Every search and delete method **requires** `tenantId` and `brandId` parameters and always adds them as `must` filters — there is no unfiltered search method.
- `tenantId`/`brandId` come from `RunContext` (JWT for API calls), never from the LLM or request body.
- Integration test: tenant A's search must never return tenant B's points, even with identical text (see Testing).
- Qdrant is not exposed outside the Docker network; API key enabled.

---

## Retrieval

### `search_knowledge_base` tool
```json
{
  "query": "string (required)",
  "sourceTypes": ["document", "product", "faq"],
  "topK": 8
}
```
Returns chunks with `text`, `title`, `headingPath`, `sourceType`, `sourceId`, `score`.

### Retrieval steps
1. Embed the query (Redis cache, 24 h TTL, key = hash of model + query).
2. Search Qdrant with tenant/brand filter, `limit = topK * 2`.
3. Drop results below the **score threshold** (start at 0.35 cosine; tuned with the evaluation set).
4. De-duplicate adjacent/overlapping chunks from the same document.
5. Optional **rerank** (see Open Decisions); keep `topK`.
6. Trim to the context budget (≤ 50% of input; AI Integration Design.md → Context Window Management).

### Low-confidence behaviour
- If no chunk passes the threshold, the tool returns an empty result with `lowConfidence: true`.
- Support Agent: replies that it doesn't know and escalates (Agent Specification.md).
- Content Agent: generates only from brand profile + product data and flags the item "no supporting knowledge" for the reviewer.

### Citations
Agents receive source ids with each chunk. Content items store `sourceRefs` (chunk source ids) so reviewers can see which documents a draft relied on.

---

## Updates and Deletion
| Event | Action |
|---|---|
| Document replaced (new version uploaded) | Ingest new version; on success delete old points by `documentId`; old MinIO object kept 30 days |
| Document deleted | Delete Qdrant points by `documentId` filter, then MinIO object, then MongoDB record |
| Product created/updated | Re-embed product chunk (upsert by deterministic ID) |
| Product deleted | Delete points by `sourceId` |
| Brand deleted | Delete all points with `brandId`; delete MinIO prefix `{tenantId}/{brandId}/` |
| Tenant deleted / offboarded | Delete all points with `tenantId`; delete MinIO prefix `{tenantId}/` (GDPR erasure) |
| Embedding model changed | Background re-index into new collection version, switch alias |

---

## Limits
Documents per brand, storage and max file size per plan are defined in **Billing and Subscriptions Design.md** (entitlement keys `kb.docsPerBrand`, `kb.storageBytes`, `kb.maxFileBytes`) and checked at upload via `IEntitlementService`.

---

## API Endpoints
```text
POST   /api/v1/knowledge/documents          (multipart upload; brandId in form)
GET    /api/v1/knowledge/documents?brandId= (list with status)
GET    /api/v1/knowledge/documents/{id}
DELETE /api/v1/knowledge/documents/{id}
POST   /api/v1/knowledge/documents/{id}/retry
GET    /api/v1/knowledge/faqs?brandId=
POST   /api/v1/knowledge/faqs
PUT    /api/v1/knowledge/faqs/{id}
DELETE /api/v1/knowledge/faqs/{id}
POST   /api/v1/knowledge/search             (test search from the portal; same filters as agents)
```
Permissions: Tenant Admin, Marketing Manager and Content Manager can manage knowledge (RBAC "Content Only" includes knowledge-base documents); Viewer is read-only.

---

## Evaluation
- **Retrieval golden set:** for 3 sample brands, 40 questions each with the expected source chunk(s).
- Metrics: **recall@8** (target ≥ 0.85), **MRR**, and rate of correct "no answer" for out-of-scope questions.
- Run in CI when chunking, threshold, embedding model or reranker changes.
- Production signal: Support escalations caused by low confidence, and reviewer flags of "unsupported claim".

## Testing
- Unit: chunker (sizes, overlap, heading paths), extractors per format, deterministic point IDs.
- Integration (Testcontainers: Qdrant, MongoDB, MinIO): full ingest → search; replace/delete flows.
- **Isolation test (required gate):** two tenants with identical documents; every search and delete path must return/affect only the caller's tenant and brand.

## Observability
- Metrics: ingestion jobs by status, ingestion duration, chunks per document, search latency p95, empty-result rate, average top score.
- Logs: `documentId`, `tenantId`, step, duration, failure reason (no document text in logs).

---

## Delivery Plan (Phase 4, 2 weeks)
| Week | Work |
|---|---|
| 1 | Upload endpoint, MinIO storage, ClamAV, `documents` lifecycle, extractors, chunker, unit tests |
| 1 | Qdrant collection + alias + payload indexes, `QdrantKnowledgeStore` with enforced filters |
| 2 | Embedding service, `IngestDocumentJob`, product/FAQ indexing via domain events |
| 2 | Retriever + `search_knowledge_base` tool, portal search test page, isolation & evaluation tests |

## Open Decisions
- **Hybrid search** (dense + keyword/sparse vectors in Qdrant) — improves matching of product names and SKUs; recommend adding if recall@8 < 0.85.
- **Reranker** (cross-encoder or provider rerank API) — adds latency and cost; decide after first evaluation.
- **Multilingual brands:** confirm the chosen embedding model handles all target languages, or store per-language collections.
- **Website crawling** and **OCR** for scanned PDFs — candidates for a post-MVP phase.
