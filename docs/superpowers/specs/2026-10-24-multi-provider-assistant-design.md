# Multi-Provider Order Assistant: Design

**Date:** 2026-10-24
**Status:** Awaiting review
**Builds on:** `2026-10-23-llm-order-assistant-design.md`. This spec replaces that spec's §7 (the Anthropic adapter) and the model-related parts of its §9 (configuration). Everything else in that spec, including the gate, the tools and the HTTP behaviour, is unchanged. Branch `feature/multi-provider-assistant`, created from `main`.

## 1. Purpose and scope

The order assistant currently talks to Claude only, through the Anthropic SDK. This change lets the same assistant run on **Claude, OpenAI, Gemini or Ollama**, selected in configuration, using **Microsoft.Extensions.AI** (`IChatClient`) as the single abstraction.

LangChain was the original request. It is a Python and JavaScript framework, so the .NET equivalent is used instead: `Microsoft.Extensions.AI`, which every provider SDK implements.

**In scope:**
- one `IChatClient`-based adapter that replaces `AnthropicAssistantModel`
- a provider factory selected by `Assistant:Provider`, one provider active at a time
- per-provider settings, API keys from configuration, and an Ollama endpoint URL
- provider error classification (503 for transient failures, 500 for configuration errors)
- documentation, including how to set up Ollama on another machine

**Out of scope:**
- running Ollama from this project (no Docker Compose service; it runs on another laptop)
- choosing a provider per request, or automatic fallback between providers
- `FunctionInvokingChatClient`, Semantic Kernel and other orchestration frameworks
- Claude-only features: server-side fallbacks, `strict` tool schemas, `effort` (removed along with their settings)
- streaming responses, prompt caching, conversation history

**Success criteria:**
- Changing `Assistant__Provider` and the provider's key or endpoint in `.env`, then running `docker compose up -d`, switches the assistant to that provider with no code change.
- The security properties are unchanged: identity comes only from the JWT, every tool call passes `ToolExecutor`, and the cross-customer integration test still passes.
- The whole automated suite runs without any API key or network access to a provider.

## 2. What stays the same

`AssistantService` (the loop, its limits and replies), `ToolExecutor` (the gate), both tools, `IAssistantModel` and `IAssistantConversation`, `AssistantController`, the 503 mappings in `GlobalExceptionHandler`, `ScriptedAssistantModel` and every integration test. No Application-layer code changes, and no provider SDK type appears outside Infrastructure.

## 3. Components (Infrastructure/Assistant)

```
ChatClientAssistantModel.cs   IAssistantModel on top of IChatClient; one implementation for all providers
ChatClientFactory.cs          builds the IChatClient for Assistant:Provider
ProviderErrors.cs             classifies provider exceptions (transient or not)
AssistantOptions.cs           updated settings (§4)
AnthropicAssistantModel.cs    deleted
```

- `ChatClientAssistantModel` is a singleton. It asks `ChatClientFactory` for the client lazily, once. When the selected provider is not configured, `StartAsync` throws `AssistantNotConfiguredException`, so the app still starts without a key.
- `ChatClientFactory` constructs the client for the selected provider:

| Provider | Client | Requires |
|---|---|---|
| `Claude` (default) | `AnthropicClient` from the `Anthropic` package, then `AsIChatClient(model)` | `ANTHROPIC_API_KEY` |
| `OpenAI` | `OpenAIClient` from `Microsoft.Extensions.AI.OpenAI`, then `GetChatClient(model).AsIChatClient()` | `OPENAI_API_KEY` |
| `Gemini` | `Google.GenAI` client, then `AsIChatClient(model)` | `GEMINI_API_KEY` |
| `Ollama` | `OllamaApiClient(endpoint, model)` from `OllamaSharp` (already an `IChatClient`) | `Assistant:Ollama:Endpoint` |

- Every client uses a 60 second timeout. The exact constructor calls for each SDK are fixed in the implementation plan from compiler output.
- **Optional overrides, used by tests and documented in the README:** `ANTHROPIC_BASE_URL` (already present) and `Assistant:OpenAI:Endpoint`, which also allows OpenAI-compatible servers.

## 4. Configuration

```json
"Assistant": {
  "Provider": "Claude",
  "MaxTokens": 4096,
  "MaxToolRounds": 5,
  "Claude": { "Model": "claude-haiku-5-5" },
  "OpenAI": { "Model": "<pinned during planning>" },
  "Gemini": { "Model": "<pinned during planning>" },
  "Ollama": { "Endpoint": "", "Model": "qwen2.5:7b" }
}
```

- `Provider` is one of `Claude`, `OpenAI`, `Gemini`, `Ollama`. Any other value makes the app fail at startup (`ValidateOnStart`).
- `MaxTokens` must be greater than 0 (default lowered from 16000 to 4096, because answers are short and some providers reject large output limits). `MaxToolRounds` must be between 1 and 10.
- The selected provider's `Model` must not be blank. `Ollama:Endpoint`, when set, must be an absolute `http` or `https` URL.
- `Effort` and `EnableFallbacks` are removed. Leftover values in the environment are ignored.
- The OpenAI and Gemini default model IDs are looked up and pinned in the implementation plan, not guessed here.
- Secrets live only in `.env` or the environment. `.env.example` gains `OPENAI_API_KEY=`, `GEMINI_API_KEY=` and `Assistant__Provider=Claude` as a documented line.
- **Not configured** means: the selected provider is Claude, OpenAI or Gemini and its key is empty, or it is Ollama and the endpoint is empty. The chat endpoint then returns 503 "Assistant is not configured." and the rest of the API works normally.

## 5. Request and response mapping

**`StartAsync(systemPrompt, tools, userMessage)`:**
- history is `[System(systemPrompt), User(userMessage)]`
- `ChatOptions`: `MaxOutputTokens = MaxTokens`, `ToolMode = Auto`, and `Tools` built from `IAssistantTool` as **declaration-only** functions (name, description, JSON schema). The client is **not** wrapped in `FunctionInvokingChatClient`, so no framework ever runs a tool. A model's tool request comes back as `FunctionCallContent`, and only `ToolExecutor` runs it.

**`ContinueAsync(results)`:**
- the response's messages are appended to the history unchanged (reasoning and signatures included)
- the results become one tool-role message with one `FunctionResultContent` per `ToolResult`, in order. Error results are plain text such as `"Order not found."`.

**Response to `ModelTurn`:**

| Response | `ModelStopReason` |
|---|---|
| contains any `FunctionCallContent` | `ToolUse`, whatever the finish reason says |
| `ChatFinishReason.Stop` | `EndTurn` |
| `ChatFinishReason.Length` | `MaxTokens` |
| `ChatFinishReason.ContentFilter` | `Refusal` |
| anything else | `Other` |

`Text` is the text parts joined with `"\n"`. Each `FunctionCallContent` becomes a `ToolCall(CallId, Name, arguments as JsonElement)`.

## 6. Errors (`ProviderErrors`)

| Condition | Result |
|---|---|
| network failure, timeout not caused by the caller, HTTP 429, HTTP 5xx | `AssistantUnavailableException` → 503 "Assistant is temporarily unavailable." |
| the Ollama laptop is off or unreachable | the same 503 |
| HTTP 400, 401, 403, 404 (bad key, unknown model) | propagates unchanged → 500, logged at Error |
| the caller cancels the request (`ct` cancelled) | `OperationCanceledException`, never mapped to 503 |

`ProviderErrors` recognises `HttpRequestException` and the status exceptions of the Anthropic, OpenAI and Google SDKs. Anything it does not recognise is treated as non-transient, so unknown failures surface as 500 instead of being hidden. The 503 cases are logged at Warning with the exception type and no message text.

## 7. Security and privacy

- No change to the security model. Model output is still untrusted input; `ToolExecutor` still allows only registered tools with strict arguments; identity still comes from `ICurrentUser`; results are still restricted DTOs; nothing sensitive is logged.
- Weaker models may send malformed arguments or pick the wrong tool. The gate rejects bad arguments with `"Invalid arguments: …"` and the model can retry within the round limit. This affects answer quality, not access control.
- **Data leaves the system.** With `OpenAI` or `Gemini`, a customer's order status, total and product names are sent to that provider. With `Ollama` they travel over plain HTTP on the local network, and Ollama has no authentication, so it must only be used on a trusted network. The README states both.

## 8. Testing

**Unit tests (no key, no real provider):**
- `ChatClientAssistantModel` with a scripted fake `IChatClient`: text and tool-call mapping; every finish-reason row; a tool call with finish reason `Stop` still maps to `ToolUse`; the history keeps the assistant message unchanged; results go in one tool message in order; tools are declaration-only; `MaxOutputTokens` comes from options.
- `ProviderErrors`: transient versus non-transient classification, and cancellation is never classified as transient.
- `ChatClientFactory` and `AssistantOptions`: the right client type per provider; missing key or endpoint means not configured; invalid `Provider`, `MaxTokens`, `MaxToolRounds` or endpoint fail validation.
- **Claude thinking echo:** a local stub server (the existing `StubAnthropicServer`) returns a thinking block with a signature plus a tool call; the test asserts that the second request sends the same thinking block and signature back, and all results in one user message. This checks `AsIChatClient()` for Claude Haiku 5.5, where thinking is on by default. **If it fails, work stops and the decision (for example turning Claude's thinking off) goes back to the user.**
- OpenAI and Ollama: a local stub server per provider confirms the real SDK returns a tool call as `FunctionCallContent` and accepts a tool result.
- Gemini: construction test only. The README gives a manual smoke test.

**Integration tests:** unchanged, using `ScriptedAssistantModel`. The "not configured" test now runs against the new adapter with no key and still expects 503.

No automated test calls a real provider.

## 9. Documentation

- **README:** a providers table with the model setting and environment variable for each, how to switch, the `ANTHROPIC_BASE_URL` and `Assistant:OpenAI:Endpoint` overrides, the data-leaves-the-system note, and the **Ollama laptop setup**: run Ollama with `OLLAMA_HOST=0.0.0.0`, open Windows Firewall port 11434, `ollama pull qwen2.5:7b`, and set `Assistant__Ollama__Endpoint=http://<laptop-IP>:11434`. It also notes that the model must support tool calling.
- **CLAUDE.md:** the assistant section now describes the `IChatClient` adapter, the factory, and how to add a provider.
- **`.env.example`:** `Assistant__Provider`, `OPENAI_API_KEY`, `GEMINI_API_KEY`, `Assistant__Ollama__Endpoint`.

## 10. Packages

Added: `Microsoft.Extensions.AI.OpenAI` 10.11.0, `Google.GenAI` 1.27.0, `OllamaSharp` 5.5.0, plus `Microsoft.Extensions.AI.Abstractions` at the highest version those packages require. Kept: `Anthropic` 13.1.0. All versions are confirmed against NuGet when the plan is written, and added to `Directory.Packages.props`.
