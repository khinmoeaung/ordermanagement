# Policy Q&A with RAG: Design

**Date:** 2026-10-24
**Status:** Awaiting review
**Builds on:** the Core API and the order assistant (`2026-10-23-*` and `2026-10-24-multi-provider-assistant-design.md`). Branch `feature/policy-rag`, created from `main`.

## 1. Purpose and scope

A logged-in user asks a question about shop policy (cancellation, refund, shipping) and gets an answer written by an LLM **only from retrieved policy text**, with citations to the policy sections it used. Retrieval is hybrid: BM25 keyword search and Qdrant semantic search, combined with Reciprocal Rank Fusion (RRF).

**In scope:**
- sample Markdown policies (`policies/cancellation.md`, `refund.md`, `shipping.md`; text in Appendix A)
- a Markdown loader and a heading-based chunker
- embeddings from Qwen3-Embedding-0.6B on a remote Ollama (Laptop B), reached through a configurable URL
- storage of chunk vectors, text and metadata in Qdrant (Docker, persistent volume)
- hybrid retrieval and RRF, then a grounded LLM answer with citations
- re-indexing that never creates duplicate chunks
- `POST /api/v1/policies/ask` and `POST /api/v1/policies/reindex`
- structured logging, error handling, configuration, tests, README

**Out of scope:** decisions about a specific order (this feature only reports what the policy says), conversation memory, streaming, rate limiting, a re-ranker, a file watcher, any document format other than Markdown.

**Success criteria:**
- `docker compose up` starts Qdrant with the API; after Ollama on Laptop B is configured, policies are indexed on startup and `ask` answers with citations.
- Asking something the policies do not cover gets an answer that says the information is not available, with no citations and no invented facts.
- Running re-index twice, or after editing one file, never duplicates a chunk; only changed chunks are re-embedded.
- The whole automated suite runs with no Ollama, LLM key or network access; only the Qdrant store tests need Docker.

## 2. Conventions

The existing solution is a layered monolith with a feature folder per area (`Auth`, `Orders`, `Assistant`). This feature follows that: `Application/Policies`, `Infrastructure/Policies`, `Api/Controllers/PoliciesController`, registered through one `AddPolicies()` extension per layer. No new projects.

## 3. Components

| Component | Layer | Responsibility |
|---|---|---|
| `PolicyChunker` | Application | Pure function: Markdown text to chunks, split by heading |
| `Bm25Index` | Application | In-memory BM25 (pure) |
| `ReciprocalRankFusion` | Application | Merges ranked lists (pure) |
| `PolicyIndexer` | Application | Loads, chunks, diffs against the store, embeds only what changed, upserts, deletes stale chunks, rebuilds BM25 |
| `PolicyRetriever` | Application | Embeds the question, runs both searches, fuses with RRF |
| `PolicyQuestionService` | Application | Retrieve, ask the LLM, extract citations |
| `IPolicyLoader`, `IPolicyEmbedder`, `IPolicyVectorStore`, `IPolicyAnswerGenerator` | Application | Ports implemented by Infrastructure |
| File loader, Ollama embedder (`IEmbeddingGenerator`), Qdrant store (`Qdrant.Client`), answer generator (`IChatClient`) | Infrastructure | All I/O |
| `PolicyIndexingHostedService` | Infrastructure | Startup indexing in `Development` and `Docker` |
| `PoliciesController` | Api | The two endpoints |

The LLM is the provider already selected by `Assistant:Provider`, through the existing `ChatClientFactory`. No new provider settings.

## 4. Policy files and chunking

- Folder: `policies/` in the repo root, configured by `Policies:Directory`. In Docker it is mounted read-only (`./policies:/app/policies:ro`), so editing a file needs a re-index but no rebuild.
- The chunker splits on headings of level 1 to 3. A chunk's text is its heading path (for example `Refund Policy > Eligibility`) followed by the body, so the heading also feeds the embedding and BM25. Sections with no body are skipped. A section longer than `Chunking:MaxChars` (default 1500) is split by paragraph into numbered parts.
- Each chunk has: source file name, document title (the level 1 heading), heading path, part number, text, and a content hash.

## 5. Qdrant storage and re-indexing

- Collection `policy_chunks`, cosine distance. The vector size comes from the first embedding (1024 for Qwen3-Embedding-0.6B). An existing collection with a different size is a clear error.
- **Point ID:** a deterministic GUID from SHA-256 of `source|headingPath|part`. Upserting the same chunk replaces it.
- **Payload:** `source`, `title`, `heading_path`, `part`, `text`, `content_hash`, `embedding_model`. The hash covers the text and the embedding model name, so changing the model re-embeds everything.
- Docker volume `qdrant-data` keeps the index across restarts.

**Re-index algorithm (idempotent):**
1. Load and chunk every file into the desired set.
2. Read every existing point's ID and hash from Qdrant.
3. New (ID absent) and changed (hash differs) chunks are embedded and upserted. Unchanged chunks cost no Ollama call.
4. Points whose ID is not in the desired set (deleted file, removed heading) are deleted.
5. The BM25 index is rebuilt from the desired set and swapped in atomically.
6. Result: `{ added, updated, unchanged, deleted, totalChunks }`. A second run with no edits reports everything unchanged.
- A lock lets only one re-index run at a time.
- At startup the BM25 index is built from the files first, then Qdrant is synced.

## 6. Retrieval

- **BM25:** `k1=1.5`, `b=0.75`. Tokenizer: lower-case, split on anything that is not a letter or digit, drop tokens under 2 characters and a short English stop-word list, trim `ing`, `ed`, `es`, `s` endings when at least 4 letters remain.
- **Semantic:** the question is embedded with the query instruction prefix (Qwen3-Embedding's recommended form; documents get none; configurable) and searched in Qdrant.
- **Fusion:** top `TopK` (8) from each list, `score = sum of 1/(RrfK + rank)` with `RrfK` 60, then the top `FinalCount` (4) chunks go to the LLM.
- A question in Burmese gets no BM25 hits (English tokenizer); RRF then uses the semantic list alone.

## 7. Answer generation

- System prompt: answer only from the numbered excerpts; if they do not contain the answer, say so and suggest contacting support; never guess or use outside knowledge; make no decision about a specific order; answer in the language of the question; cite with `[1]`.
- The customer's question is passed as clearly delimited, untrusted user text. The excerpts are our own policy text.
- Citations are built by the backend: it parses the `[n]` markers in the answer and returns only the excerpts the answer cites. The LLM cannot invent a file name. An answer with no `[n]` has no citations.
- If retrieval returns no chunks the LLM is not called (see §9).

## 8. API

`POST /api/v1/policies/ask`, any logged-in user:
```json
{ "question": "Can I cancel my order?" }
```
```json
{ "answer": "Yes, while the order is Pending or Confirmed [1]. ...",
  "citations": [ { "source": "cancellation.md", "title": "Cancellation Policy",
                   "section": "Cancellation Policy > When an order can be cancelled",
                   "excerpt": "An order can be cancelled while its status is Pending or Confirmed ..." } ] }
```
The question is required and at most 1000 characters (400 otherwise). `excerpt` is the first 300 characters of the chunk.

`POST /api/v1/policies/reindex`, Admin only: returns the `{ added, updated, unchanged, deleted, totalChunks }` result.

## 9. Errors and logging

All errors are ProblemDetails with a `traceId`. The handler logs the 503 cases at Warning with exception type names only.

| Situation | Response |
|---|---|
| Embedding endpoint not configured | 503 "Policy search is not configured." |
| Laptop B or Qdrant unreachable, timeout, 429, 5xx | 503 "Policy search is temporarily unavailable." There is no silent fallback to BM25 alone, because a Burmese question would then wrongly look "not covered". |
| Nothing indexed yet | 503 "Policies have not been indexed yet." (no LLM call) |
| LLM provider not configured or down | the existing assistant 503s |
| Missing token / not admin / bad input | 401 / 403 / 400 |

Logging is structured: a summary per re-index, and per question the stage durations, the hit counts and the chosen chunk IDs. The question text, the answer, policy text and API keys are never logged.

## 10. Configuration and Docker

```json
"Policies": {
  "Directory": "policies",
  "Embedding": { "Endpoint": "", "Model": "qwen3-embedding:0.6b",
                 "QueryInstruction": "Given a customer question about shop policies, retrieve the policy passages that answer it" },
  "Qdrant":    { "Host": "qdrant", "GrpcPort": 6334, "Collection": "policy_chunks", "ApiKey": "" },
  "Chunking":  { "MaxChars": 1500 },
  "Retrieval": { "TopK": 8, "FinalCount": 4, "RrfK": 60 }
}
```

- The Laptop B URL and the Qdrant key come from `.env` (`Policies__Embedding__Endpoint`, `Policies__Qdrant__ApiKey`), never from source.
- Options are validated at startup: `TopK` and `FinalCount` above 0 and `FinalCount` at most `TopK`; `MaxChars` above 0; the endpoint, when set, an absolute http(s) URL.
- `docker-compose.yml` gains a `qdrant` service with a pinned image version, the `qdrant-data` volume, and ports 6333 and 6334 bound to `127.0.0.1`. The API depends on it.
- Startup indexing runs in `Development` and `Docker` only. If Qdrant or Laptop B is down the API still starts, with a Warning.

## 11. Testing

**Unit (no network):** chunker (heading split, empty sections skipped, oversize split, stable IDs, hash); BM25 (ranking, a Burmese query returns nothing); RRF (worked example, one list empty); indexer with in-memory store and embedder (added, updated, unchanged, deleted; a second run calls the embedder zero times; a model change re-embeds); retriever; question service (citation parsing, no chunks means no LLM call); prompt builder; Ollama embedder against the local stub server; Markdown loader on a temp folder.

**Qdrant (Testcontainers, real Qdrant):** upserting twice leaves one point per ID; delete; search; the indexer with a deterministic fake embedder: a second run changes nothing, editing one file updates only its chunks.

**API integration:** 401, 403, 400; `ask` with fakes returns answer and citations; `reindex` admin only; the not-configured and not-indexed 503s.

## 12. Documentation and packages

README gets a "Policy Q&A (RAG)" section: Laptop B setup (`ollama pull qwen3-embedding:0.6b`, `OLLAMA_HOST=0.0.0.0`, firewall port 11434), `docker compose up`, indexing, request and response examples, how to test, and the note that Burmese questions rely on semantic search alone. CLAUDE.md and `.env.example` are updated.

New packages: `Qdrant.Client` and `Testcontainers.Qdrant` (versions fixed in the plan from NuGet). `OllamaSharp` and `Microsoft.Extensions.AI.Abstractions` are already present. No Markdown parsing library: headings are found line by line.

## Appendix A: sample policy files

These three files are created verbatim in `policies/`. They describe a fictional shop and match the system's real rules (an order can be cancelled only while Pending or Confirmed, and cancelling returns the items to stock).

### `policies/cancellation.md`

```markdown
# Cancellation Policy

## Who can cancel
Customers can cancel their own orders. Our support team can also cancel an order on a customer's behalf.

## When an order can be cancelled
An order can be cancelled while its status is **Pending** or **Confirmed**.
- **Pending:** we have received the order but have not confirmed it yet.
- **Confirmed:** we have accepted the order and are preparing it.

Once an order is **Shipped**, **Delivered** or **Completed** it can no longer be cancelled. After delivery, use the return process in the Refund Policy instead.

## How to cancel
Open the order in your account and choose Cancel order, or contact support with your order number (for example ORD-20261024-AB12CD). The cancellation takes effect immediately and the order status changes to **Cancelled**.

## What happens after a cancellation
- The items go back into our stock straight away.
- If you already paid, you receive a full refund to your original payment method. See the Refund Policy for timing.
- A cancelled order cannot be reopened. Place a new order if you still want the items.

## Cancelling part of an order
Orders are cancelled as a whole. To remove one item, cancel the order while it is still Pending or Confirmed and place a new order with the items you want.
```

### `policies/refund.md`

```markdown
# Refund Policy

## Eligibility
You can request a refund within **14 days** of the delivery date if:
- the item is unused and in its original packaging, or
- the item arrived damaged, defective or different from what you ordered.

## Items that cannot be refunded
- Gift cards and digital goods
- Items marked as final sale
- Items that were used, washed or damaged after delivery
- Hygiene products that have been opened

## How to request a refund
Contact support with your order number and a short reason. For damaged or defective items, include a photo. We reply within **2 business days** and send a return label if the item has to be sent back.

## Refund amount
- Eligible returns are refunded the full item price.
- The original shipping cost is refunded only when the item was damaged, defective or not what you ordered.
- Return shipping is paid by the customer, unless the item was damaged, defective or not what you ordered.

## When you receive your money
After we receive and inspect the returned item, the refund is issued to your original payment method within **5 to 7 business days**. Your bank may need extra time to show it.

## Refunds for cancelled orders
If you cancel an order before it ships, you receive a full refund, including shipping, within 5 to 7 business days.
```

### `policies/shipping.md`

```markdown
# Shipping Policy

## Processing time
Orders are prepared within **1 to 2 business days** after they are confirmed. Orders placed on weekends or public holidays are processed on the next business day.

## Delivery options and costs
- **Standard shipping:** 3 to 5 business days, flat fee of $4.99.
- **Express shipping:** 1 to 2 business days, flat fee of $12.99.
- Standard shipping is free on orders over $50.

## Where we ship
We currently ship within the country only. We do not ship to PO boxes.

## Tracking your order
When your order is shipped, its status changes to **Shipped** and a tracking number is added to the order details. Delivery times are counted from the day the order ships.

## Changing the delivery address
You can change the delivery address only while the order is **Pending** or **Confirmed**. Contact support with your order number. Once the order has shipped, the address cannot be changed and you will need to contact the carrier.

## Delayed or lost parcels
If your parcel has not arrived 3 business days after the estimated delivery date, contact support. We open an investigation with the carrier and either send a replacement or refund you within 10 business days. For parcels that arrive damaged, see the Refund Policy.
```
