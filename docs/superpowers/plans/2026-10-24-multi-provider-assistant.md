# Multi-Provider Order Assistant Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the order assistant run on Claude, OpenAI, Gemini or Ollama, chosen in configuration, through one `Microsoft.Extensions.AI` (`IChatClient`) adapter.

**Architecture:**
- Replace `AnthropicAssistantModel` with `ChatClientAssistantModel`, an `IAssistantModel` over `IChatClient`.
- `ChatClientFactory` builds the client for `Assistant:Provider`.
- The loop (`AssistantService`), the gate (`ToolExecutor`), the tools and every integration test are unchanged.
- Tools are sent as declaration-only functions, and the client is never wrapped in `FunctionInvokingChatClient`, so only `ToolExecutor` runs a tool.

**Tech Stack:** the existing .NET 10 solution (see `CLAUDE.md`), plus `Microsoft.Extensions.AI.Abstractions` 10.11.0, `Microsoft.Extensions.AI.OpenAI` 10.11.0, `Google.GenAI` 1.27.0, `OllamaSharp` 5.5.0. `Anthropic` stays at 13.1.0.

**Spec:** `docs/superpowers/specs/2026-10-24-multi-provider-assistant-design.md`. Section numbers (§N) refer to it. It replaces §7 and the model parts of §9 of `2026-10-23-llm-order-assistant-design.md`.

## Global Constraints

- Follow `CLAUDE.md`: layering, central package versions, test-first, one-line commit messages with no trailers. Branch `feature/multi-provider-assistant`.
- Provider SDK types appear only in `src/OrderManagement.Infrastructure/Assistant/*`. Nothing in Application, Api or `IAssistantModel` changes.
- The client is never wrapped in `FunctionInvokingChatClient`. Tools are `AIFunctionFactory.CreateDeclaration(name, description, schema)` declarations only.
- `Provider` values: `Claude` (default), `OpenAI`, `Gemini`, `Ollama`. Env var names: `ANTHROPIC_API_KEY`, `OPENAI_API_KEY`, `GEMINI_API_KEY`. Ollama uses `Assistant:Ollama:Endpoint`. Optional overrides: `ANTHROPIC_BASE_URL`, `Assistant:OpenAI:Endpoint`.
- **Defaults:** `MaxTokens` 4096, `MaxToolRounds` 5 (valid 1–10), every client timeout 60 seconds.
- **Model defaults:** Claude `claude-haiku-5-5`, Ollama `qwen2.5:7b`, OpenAI `gpt-5-mini`, Gemini `gemini-3.5-flash`. The last two are **unverified best guesses** (official model lists could not be confirmed): the README must say to confirm them against the provider's model list.
- `Effort` and `EnableFallbacks` settings are removed.
- The 503 detail strings stay `"Assistant is not configured."` and `"Assistant is temporarily unavailable."`.
- Never log message text, tool arguments, or provider exception messages. Log exception types only.
- No automated test calls a real provider. Integration tests on this machine need `DOCKER_API_VERSION=1.41`.

## Review Focus

1. **A model returns a tool call but a finish reason of `Stop`** (Ollama and Gemini do this). It must still be a tool turn. The test is in Task 2.
2. **The Ollama laptop is off or unreachable.** The customer must get 503 "temporarily unavailable", not a 500. The test is in Task 4.
3. **The caller cancels the request.** It must surface as a cancellation, never as 503. Tests are in Tasks 1, 2 and 4.
4. **A tool call with `null` or missing arguments.** It must reach the gate as an empty JSON object (so the gate answers "Invalid arguments"), never throw. The test is in Task 2.
5. **A provider that sends tool calls without IDs** (Ollama). Each call must get a distinct non-empty ID, and its result must be sent back under that same ID. The test is in Task 2.

---

## File structure

```
Directory.Packages.props, src/OrderManagement.Infrastructure/OrderManagement.Infrastructure.csproj   (modify: packages)
src/OrderManagement.Infrastructure/Assistant/
  ProviderErrors.cs              new
  ChatClientAssistantModel.cs    new
  ChatClientFactory.cs           new
  AssistantOptions.cs            rewritten
  AnthropicAssistantModel.cs     deleted
src/OrderManagement.Infrastructure/DependencyInjection.cs   (modify)
src/OrderManagement.Api/appsettings.json, Infrastructure/GlobalExceptionHandler.cs   (modify)
tests/OrderManagement.UnitTests/
  Fakes/ScriptedChatClient.cs                                   new
  Infrastructure/ProviderErrorsTests.cs, ChatClientAssistantModelTests.cs,
                 AssistantOptionsTests.cs, ChatClientFactoryTests.cs, ProviderRoundTripTests.cs   new
  Infrastructure/StubAnthropicServer.cs → StubApiServer.cs      renamed in Task 4
  Infrastructure/AnthropicAssistantModelTests.cs                deleted in Task 3
tests/OrderManagement.IntegrationTests/ApiFactory.cs, Api/AssistantEndpointsTests.cs   (modify)
.env.example, README.md, CLAUDE.md   (modify)
```

---

### Task 1: Packages and `ProviderErrors`

**Files:**
- Modify: `Directory.Packages.props`, `src/OrderManagement.Infrastructure/OrderManagement.Infrastructure.csproj`
- Create: `src/OrderManagement.Infrastructure/Assistant/ProviderErrors.cs`
- Test: `tests/OrderManagement.UnitTests/Infrastructure/ProviderErrorsTests.cs`

**Interfaces:**
- Produces: `public static class ProviderErrors { public static bool IsTransient(Exception ex, CancellationToken ct); }`
- Packages added centrally and referenced by Infrastructure: `Microsoft.Extensions.AI.Abstractions` 10.11.0, `Microsoft.Extensions.AI.OpenAI` 10.11.0, `Google.GenAI` 1.27.0, `OllamaSharp` 5.5.0. If the restore reports a version conflict with `Anthropic` 13.1.0, resolve it by raising Abstractions to the highest version required and note it in the report.

`IsTransient` rules, in order:
1. `ct.IsCancellationRequested` → `false`. Cancellation is handled by the caller, never mapped to 503.
2. `HttpRequestException`: `StatusCode` null → `true`; 429 or ≥500 → `true`; any other status → `false`.
3. `TaskCanceledException` or `TimeoutException` → `true` (the caller did not cancel, so it is a provider timeout).
4. `IOException` or `SocketException` (connection refused or reset) → `true`.
5. Anthropic: `AnthropicRateLimitException`, `Anthropic5xxException`, `AnthropicIOException` → `true`; any other `AnthropicApiException` → `false`.
6. `System.ClientModel.ClientResultException` (OpenAI): `Status` 429 or ≥500 → `true`; `Status` 0 → decide on the inner exception; otherwise `false`.
7. Google: `Google.GenAI.ServerError` → `true`; `ClientError` whose status code is 429 → `true` (read the code from the exception's property, named from the compiler); other `ClientError` → `false`.
8. OllamaSharp `OllamaException` (including `ModelDoesNotSupportToolsException`) → `false`.
9. Anything else: `false` after checking `InnerException` recursively for rules 2–4.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void Cancelled_token_is_never_transient()                 // HttpRequestException(no status) with a cancelled ct → false
[Theory, InlineData(null), InlineData(429), InlineData(500), InlineData(503)] public void Http_request_exception_transient_statuses(int? status)
[Theory, InlineData(400), InlineData(401), InlineData(404)] public void Http_request_exception_client_errors_are_not_transient(int status)
[Fact] public void Timeout_without_caller_cancel_is_transient()          // TaskCanceledException, token not cancelled → true
[Fact] public void Socket_and_io_errors_are_transient()                  // SocketException, IOException → true
[Fact] public void Inner_exception_is_checked()                          // Exception wrapping HttpRequestException(no status) → true
[Fact] public void Unknown_exception_is_not_transient()                  // InvalidOperationException → false
```

Types that can't be constructed in a unit test (Anthropic and OpenAI status exceptions, Google errors) are covered in Task 4 through stub servers where possible; the Google rule is checked by compilation only.

- [ ] **Step 2:** Run `dotnet test --project tests/OrderManagement.UnitTests --filter "FullyQualifiedName~ProviderErrors"` and expect FAIL to compile.
- [ ] **Step 3:** Add the packages, then implement `ProviderErrors`.
- [ ] **Step 4:** Run the filter and expect PASS, then `dotnet build` with 0 warnings.
- [ ] **Step 5:** Commit: `Add provider packages and error classification`

---

### Task 2: `ChatClientAssistantModel`

**Files:**
- Create: `src/OrderManagement.Infrastructure/Assistant/ChatClientAssistantModel.cs`, `tests/OrderManagement.UnitTests/Fakes/ScriptedChatClient.cs`
- Test: `tests/OrderManagement.UnitTests/Infrastructure/ChatClientAssistantModelTests.cs`

**Interfaces:**
- Consumes: `IAssistantModel`, `IAssistantConversation`, `IAssistantTool`, `ModelTurn`, `ModelStopReason`, `ToolCall`, `ToolResult`, `AssistantNotConfiguredException`, `AssistantUnavailableException` (Application); `ProviderErrors` (Task 1).
- Produces:
  - `public sealed class ChatClientAssistantModel : IAssistantModel` with the constructor `ChatClientAssistantModel(Func<IChatClient?> clientFactory, int maxTokens)`. The factory is called lazily, at most once (`Lazy<IChatClient?>`).
  - `ScriptedChatClient : IChatClient` (test fake): a queue of `ChatResponse`s or exceptions. It records a **snapshot copy** of the messages and the `ChatOptions` for every `GetResponseAsync` call (the model mutates its history list).

Behavior (spec §5 and §6):
- `StartAsync` throws `AssistantNotConfiguredException` when the factory returns `null`.
- First request messages are `[System(systemPrompt), User(userMessage)]`. `ChatOptions`: `MaxOutputTokens = maxTokens`, `ToolMode = ChatToolMode.Auto`, `Tools` = one `AIFunctionFactory.CreateDeclaration(tool.Name, tool.Description, tool.InputSchema, null)` per tool. These must not be `AIFunction` instances.
- After each response, `response.Messages` are appended to the history unchanged.
- `ContinueAsync` appends one `ChatMessage(ChatRole.Tool, …)` holding one `FunctionResultContent(callId, content)` per result, in order, then sends again.
- `ModelTurn.ToolCalls` come from every `FunctionCallContent` in the response. `Arguments` null → `{}`. A blank `CallId` is replaced with a generated unique non-empty ID, **and the history's `FunctionCallContent` keeps that same ID** so the result matches.
- Stop reason: any `FunctionCallContent` → `ToolUse`; else `Stop` → `EndTurn`; `Length` → `MaxTokens`; `ContentFilter` → `Refusal`; else `Other`. `Text` is the non-empty text parts joined with `"\n"`.
- A provider exception for which `ProviderErrors.IsTransient(ex, ct)` is true becomes `AssistantUnavailableException(ex)`. Everything else, including cancellation, propagates unchanged.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public async Task Start_without_client_throws_not_configured()
[Fact] public async Task Client_factory_is_called_once_across_conversations()
[Fact] public async Task First_request_has_system_and_user_messages_and_declaration_only_tools()   // tools not AIFunction; ToolMode Auto; MaxOutputTokens == 4096
[Fact] public async Task Text_response_maps_to_EndTurn_and_joins_parts_with_newline()
[Fact] public async Task Tool_call_maps_to_ToolUse_even_when_finish_reason_is_Stop()               // Review Focus 1
[Theory, InlineData(Length→MaxTokens), InlineData(ContentFilter→Refusal), InlineData(ToolCalls with no call content→Other)] public async Task Other_finish_reasons_map()
[Fact] public async Task Null_arguments_become_empty_object()                                      // Review Focus 4: Input.GetRawText() == "{}"
[Fact] public async Task Arguments_are_passed_as_json()                                            // {"orderNumber":"ORD-20260101-ABCDEF"}
[Fact] public async Task Blank_call_ids_are_replaced_with_distinct_ids_and_results_use_them()      // Review Focus 5: two calls, ids ""/null → distinct non-empty; the Tool message's FunctionResultContent.CallId matches
[Fact] public async Task Continue_keeps_assistant_message_and_sends_one_tool_message_in_order()    // history = System, User, Assistant(original instance), Tool(2 results)
[Fact] public async Task Transient_provider_error_becomes_unavailable()                            // HttpRequestException(503)
[Fact] public async Task Non_transient_provider_error_propagates()                                 // HttpRequestException(401) stays HttpRequestException
[Fact] public async Task Caller_cancellation_propagates_as_cancellation()                          // cancelled ct → OperationCanceledException, not AssistantUnavailableException (Review Focus 3)
```

- [ ] **Step 2:** Run `--filter "FullyQualifiedName~ChatClientAssistantModel"` and expect FAIL to compile.
- [ ] **Step 3:** Implement the model, with a private `Conversation : IAssistantConversation` nested class holding the history and options.
- [ ] **Step 4:** Run the filter and then the whole unit suite. Expect PASS.
- [ ] **Step 5:** Commit: `Add IChatClient assistant model adapter`

---

### Task 3: Options, `ChatClientFactory`, wiring, and removing the Anthropic adapter

**Files:**
- Rewrite: `src/OrderManagement.Infrastructure/Assistant/AssistantOptions.cs`
- Create: `src/OrderManagement.Infrastructure/Assistant/ChatClientFactory.cs`
- Modify: `src/OrderManagement.Infrastructure/DependencyInjection.cs`, `src/OrderManagement.Api/appsettings.json`, `tests/OrderManagement.IntegrationTests/ApiFactory.cs`
- Delete: `src/OrderManagement.Infrastructure/Assistant/AnthropicAssistantModel.cs`, `tests/OrderManagement.UnitTests/Infrastructure/AnthropicAssistantModelTests.cs`
- Test: `tests/OrderManagement.UnitTests/Infrastructure/AssistantOptionsTests.cs`, `ChatClientFactoryTests.cs`

**Interfaces:**
- Consumes: `ChatClientAssistantModel` (Task 2).
- Produces:
  - `public enum AssistantProvider { Claude, OpenAI, Gemini, Ollama }`
  - `AssistantOptions` (section `"Assistant"`): `Provider` (default `Claude`), `MaxTokens` (4096), `MaxToolRounds` (5), and per-provider sections `Claude { Model }`, `OpenAI { Model, Endpoint }`, `Gemini { Model }`, `Ollama { Endpoint, Model }` with the defaults from Global Constraints. `public static bool Validate(AssistantOptions o)`: `MaxTokens > 0`, `MaxToolRounds` between 1 and 10, the **selected** provider's `Model` not blank, and `Ollama.Endpoint` (when not blank) an absolute `http` or `https` URI, `OpenAI.Endpoint` likewise.
  - `public sealed class ChatClientFactory(IOptions<AssistantOptions> options, IConfiguration configuration)` with `public IChatClient? Create()`, which returns `null` when the selected provider is not configured (Claude/OpenAI/Gemini: its key setting is blank; Ollama: endpoint blank).
  - Client construction (confirm exact calls from compiler output, as SDK constructors vary):
    - Claude: `new AnthropicClient { ApiKey, Timeout = 60 s, BaseUrl = ANTHROPIC_BASE_URL when set }.AsIChatClient(model, maxTokens)`. Keep the default `AnthropicThinkingMode.Adaptive`.
    - OpenAI: `new OpenAIClient(new ApiKeyCredential(key), new OpenAIClientOptions { Endpoint when set, NetworkTimeout = 60 s }).GetChatClient(model).AsIChatClient()`.
    - Gemini: `new Google.GenAI.Client(apiKey: key)` (apply a 60 s timeout through `HttpOptions` if the SDK exposes one, and say in the report if not) then `.AsIChatClient(model)`.
    - Ollama: `new OllamaApiClient(new HttpClient { BaseAddress = new Uri(endpoint), Timeout = 60 s }, model)`.
  - DI: register `ChatClientFactory` as a singleton, and `IAssistantModel` as a singleton `new ChatClientAssistantModel(factory.Create, options.MaxTokens)`. Keep the existing `IAssistantService` registration with `MaxToolRounds`. Options keep `ValidateOnStart` with `AssistantOptions.Validate`.
  - `ApiFactory.WithRealAssistantModel()` now registers `new ChatClientAssistantModel(sp.GetRequiredService<ChatClientFactory>().Create, 4096)`.
  - `appsettings.json` `Assistant` section becomes the shape in spec §4 (remove `Model`, `Effort`, `EnableFallbacks` at the top level).

- [ ] **Step 1: Write the failing tests**

```csharp
// AssistantOptionsTests
[Fact] public void Defaults_are_valid()
[Theory, InlineData(0), InlineData(-1)] public void Non_positive_max_tokens_invalid(int v)
[Theory, InlineData(0), InlineData(11)] public void Tool_rounds_out_of_range_invalid(int v)
[Fact] public void Blank_model_of_selected_provider_is_invalid()               // Provider OpenAI, OpenAI.Model "" → false
[Fact] public void Blank_model_of_unselected_provider_is_ignored()             // Provider Claude, OpenAI.Model "" → true
[Theory, InlineData("not a url"), InlineData("/relative")] public void Bad_endpoint_invalid(string url)   // Ollama.Endpoint, Provider Ollama
[Fact] public void Provider_name_binds_case_insensitively()                    // "ollama" → AssistantProvider.Ollama
[Fact] public void Unknown_provider_fails_to_bind()                            // "Gemini2" → exception when .Value is read
// ChatClientFactoryTests (in-memory IConfiguration)
[Theory, InlineData(Claude), InlineData(OpenAI), InlineData(Gemini)] public void Missing_key_means_not_configured(provider)   // Create() == null
[Fact] public void Blank_ollama_endpoint_means_not_configured()
[Fact] public void Claude_with_key_creates_client_for_configured_model()       // GetService<ChatClientMetadata>().DefaultModelId == "claude-haiku-5-5"
[Fact] public void OpenAI_with_key_creates_client_for_configured_model()
[Fact] public void Gemini_with_key_creates_a_client()                          // non-null (construction only)
[Fact] public void Ollama_with_endpoint_creates_client_for_configured_model()  // DefaultModelId == "qwen2.5:7b"
```

- [ ] **Step 2:** Run `--filter "FullyQualifiedName~AssistantOptions|FullyQualifiedName~ChatClientFactory"` and expect FAIL to compile.
- [ ] **Step 3:** Implement, wire, update `appsettings.json` and `ApiFactory`, and delete the Anthropic adapter and its test file.
- [ ] **Step 4:** Run `dotnet build` (0 warnings), the full unit suite, then `DOCKER_API_VERSION=1.41 dotnet test --project tests/OrderManagement.IntegrationTests --filter-class "*AssistantEndpointsTests"`. All must pass, including the "not configured" test returning 503 through the new adapter.
- [ ] **Step 5:** Commit: `Select assistant provider by configuration`

---

### Task 4: Provider round-trip tests against stub servers (the Claude thinking gate)

**Files:**
- Rename: `tests/OrderManagement.UnitTests/Infrastructure/StubAnthropicServer.cs` → `StubApiServer.cs` (class `StubApiServer`, same behavior; update usages)
- Test: `tests/OrderManagement.UnitTests/Infrastructure/ProviderRoundTripTests.cs`

**Interfaces:**
- Consumes: `ChatClientFactory`, `ChatClientAssistantModel`, `AssistantOptions` (Task 3), `StubApiServer`.
- Each test builds the real client through `ChatClientFactory` with an in-memory `IConfiguration` pointing at the stub (`ANTHROPIC_BASE_URL`, `Assistant:OpenAI:Endpoint`, `Assistant:Ollama:Endpoint`), wraps it in `ChatClientAssistantModel`, and uses a small stub `IAssistantTool`.
- Canned bodies the stub returns: Claude: Messages API JSON (`stop_reason`, `content` blocks, as the previous adapter tests used); OpenAI: `chat.completion` JSON with `choices[0].message.tool_calls` and `finish_reason`; Ollama: a single-line `/api/chat` JSON with `message.tool_calls` and `"done": true`.

- [ ] **Step 1: Write the failing tests**

```csharp
// Claude
[Fact] public async Task Claude_tool_turn_maps_to_tool_calls()
[Fact] public async Task Claude_echoes_thinking_with_signature_and_sends_all_results_in_one_user_message()
    // GATE: stub returns thinking(signature "sig-123") + two tool_use blocks, then an end_turn. After ContinueAsync with two results,
    // the SECOND request body has the assistant message containing the thinking block with signature "sig-123" and both tool_use ids,
    // and ONE user message containing both tool_result blocks.
[Fact] public async Task Claude_server_error_after_retries_becomes_unavailable()   // stub always 500 → AssistantUnavailableException; 3 requests seen
[Fact] public async Task Claude_rate_limit_becomes_unavailable()                    // stub 429
[Fact] public async Task Claude_unauthorized_propagates_and_is_not_unavailable()    // stub 401 → not AssistantUnavailableException
[Fact] public async Task Claude_caller_cancellation_is_not_unavailable()            // already-cancelled ct → OperationCanceledException
// OpenAI
[Fact] public async Task OpenAI_tool_call_maps_to_ToolUse_and_result_is_sent_back()  // second request has a message with role "tool" and the matching tool_call_id
[Fact] public async Task OpenAI_server_error_becomes_unavailable()                    // stub 500/429
// Ollama
[Fact] public async Task Ollama_tool_call_maps_to_ToolUse_and_result_is_sent_back()   // second request has a message with role "tool"
[Fact] public async Task Ollama_unreachable_becomes_unavailable()                     // endpoint on a closed local port → AssistantUnavailableException (Review Focus 2)
```

- [ ] **Step 2:** Run `--filter "FullyQualifiedName~ProviderRoundTrip"` and expect FAIL.
- [ ] **Step 3:** Fix whatever the tests expose in `ChatClientFactory`, `ChatClientAssistantModel` or `ProviderErrors`. **If `Claude_echoes_thinking_with_signature…` cannot be made to pass, STOP and report BLOCKED with the request bodies seen.** Do not work around it by hand-editing history or dropping thinking: the decision (for example, configuring Claude's thinking mode or turning it off) belongs to the user.
- [ ] **Step 4:** Run the filter, then the full unit suite. Expect PASS.
- [ ] **Step 5:** Commit: `Add provider round-trip tests`

---

### Task 5: 503 logging, integration coverage and documentation

**Files:**
- Modify: `src/OrderManagement.Api/Infrastructure/GlobalExceptionHandler.cs`, `tests/OrderManagement.IntegrationTests/Api/AssistantEndpointsTests.cs`, `.env.example`, `README.md`, `CLAUDE.md`
- Create: `tests/OrderManagement.IntegrationTests/CapturingLoggerProvider.cs`

**Interfaces:**
- `GlobalExceptionHandler`: when the exception is `AssistantNotConfiguredException` or `AssistantUnavailableException`, log at **Warning** with the exception **type name** (and, for unavailable, the inner exception's type name). Never log the message text of either exception or of the inner one.
- `CapturingLoggerProvider : ILoggerProvider` records `(category, level, message)`. `ApiFactory` registers it with `WithWebHostBuilder(... ConfigureLogging ...)` for the test that needs it.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public async Task Unavailable_provider_returns_503_and_logs_type_only()
    // script a step that throws new AssistantUnavailableException(new HttpRequestException("inner-secret-detail")):
    // 503 problem with "Assistant is temporarily unavailable."; a Warning exists from the exception handler containing "AssistantUnavailableException"
    // and "HttpRequestException"; no captured log entry contains "inner-secret-detail"
[Fact] public async Task Not_configured_is_logged_at_warning()                       // existing not-configured request; a Warning entry exists
```

- [ ] **Step 2:** Run `DOCKER_API_VERSION=1.41 dotnet test --project tests/OrderManagement.IntegrationTests --filter-class "*AssistantEndpointsTests"` and expect the two new tests to FAIL.
- [ ] **Step 3:** Implement the logging.
- [ ] **Step 4: Documentation.**
  - **README, "Order assistant" section rewritten:**
    - a providers table: provider, setting, key or endpoint
    - how to switch: set `Assistant__Provider=<name>` plus that provider's key or endpoint in `.env`, then `docker compose up -d`
    - the model defaults, with a clear note that the OpenAI and Gemini defaults must be confirmed against the provider's model list
    - the `ANTHROPIC_BASE_URL` and `Assistant:OpenAI:Endpoint` overrides (testing and OpenAI-compatible servers)
    - **Ollama on another machine:** run Ollama with `OLLAMA_HOST=0.0.0.0`, open Windows Firewall port 11434, `ollama pull qwen2.5:7b`, set `Assistant__Ollama__Endpoint=http://<laptop-IP>:11434`; the model must support tool calling
    - the data warning: OpenAI and Gemini receive the customer's order status, total and product names; Ollama traffic is plain HTTP with no authentication, so use it only on a trusted network
  - **`.env.example`:** `Assistant__Provider=Claude`, `OPENAI_API_KEY=`, `GEMINI_API_KEY=`, `Assistant__Ollama__Endpoint=` and `Assistant__Ollama__Model=qwen2.5:7b`.
  - **CLAUDE.md "Assistant" section:** the `IChatClient` adapter and factory replace the Anthropic adapter; tools are declaration-only and the client is never wrapped in `FunctionInvokingChatClient`; how to add a provider (a `Provider` value, factory case, options section, `ProviderErrors` rule, round-trip test); the test fakes (`ScriptedChatClient`, `StubApiServer`).
- [ ] **Step 5:** Run the full suite: `DOCKER_API_VERSION=1.41 dotnet test` (use a timeout; a previous run once stalled in container start-up). Expect everything to pass with 0 warnings. Commit: `Log assistant outages and document providers`
