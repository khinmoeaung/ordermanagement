# LLM Order Assistant Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `POST /api/v1/assistant/chat`, where a customer's plain-language question about their orders is answered by Claude through two read-only tools. Every tool call is checked by a backend gate that authorizes against the JWT user, never against what the LLM says.

**Architecture:**
- **Application** owns the tool loop (`AssistantService`), the gate (`ToolExecutor`) and the tools. They talk to the model only through `IAssistantModel` / `IAssistantConversation`.
- **Infrastructure** implements that interface with the official `Anthropic` SDK and keeps the SDK message list inside the conversation object.
- **The tools** call the existing `IOrderService`, which enforces ownership through `ICurrentUser`.

**Tech Stack:** the existing .NET 10 solution (see `CLAUDE.md`), plus the NuGet package `Anthropic` **13.1.0**.

**Spec:** `docs/superpowers/specs/2026-10-23-llm-order-assistant-design.md`. Section numbers (§N) refer to it.

## Global Constraints

- Follow `CLAUDE.md`: layering, `<Feature>Contracts.cs` per feature, central package versions, no hand edits to generated files, test-first, and one-line commit messages with no trailers. Branch `feature/llm-assistant`.
- No Anthropic SDK type may appear in Application or Api. Only `src/OrderManagement.Infrastructure/Assistant/*` references the `Anthropic` package.
- Tools never take a user ID, customer ID or role from model input. Identity comes only from `ICurrentUser`.
- **Exact strings (copy verbatim):**
  - `"Unknown tool."`
  - `"Invalid arguments: <reason>"`
  - `"Order not found."`
  - `"Tool failed."`
  - Fallback: `"Sorry, I couldn't complete that request. Please try again."`
  - Refusal: `"Sorry, I can't help with that request."`
  - 503 details: `"Assistant is not configured."` and `"Assistant is temporarily unavailable."`
- The order number pattern is `^ORD-\d{8}-[A-Z0-9]{6}$`, checked after trim and upper-casing.
- **`list_my_orders`:** Page 1, PageSize 10.
- **Chat message:** required, not whitespace, at most 2000 characters.
- **Configuration section `"Assistant"`:**
  - `Model` = `claude-opus-5-5`
  - `MaxTokens` = 16000
  - `Effort` = `low`
  - `MaxToolRounds` = 5 (valid range 1–10)
  - `EnableFallbacks` = true
- **API key:** read from the configuration key `ANTHROPIC_API_KEY`, which includes the environment variable. If it's empty, the assistant isn't configured.
- Never log customer message text, model replies or tool argument values.
- No automated test may call the real Anthropic API. Integration tests set `ANTHROPIC_API_KEY` to `""` in `ApiFactory` configuration.
- Integration tests on this machine need `DOCKER_API_VERSION=1.41`.

## Review Focus

1. **A customer types the order number in lower case or with spaces** (` ord-20261023-ab12cd `). It must normalize and match, not be rejected. The test is in Task 2.
2. **The model sends tool input that isn't a JSON object** (`null`, a string, an array). This must give the `"Invalid arguments: …"` result, never an exception or a 500. Tests are in Tasks 2 and 3.
3. **The client disconnects while a tool is running.** `OperationCanceledException` must propagate as a cancellation, not be swallowed as `"Tool failed."`. The test is in Task 3.
4. **A customer with no orders asks for their orders.** `list_my_orders` returns `{ orders: [], totalCount: 0 }` as a normal result, not an error. The test is in Task 2.
5. **A message of exactly 2000 characters** is accepted and 2001 is rejected. A message of only whitespace gives 400. Tests are in Task 4.

---

## File structure

```
src/OrderManagement.Application/
  Orders/OrderContracts.cs                (modify: + GetByOrderNumberAsync, + GetByNumberAsync)
  Orders/OrderService.cs                  (modify: + GetByNumberAsync)
  Assistant/AssistantContracts.cs         ChatRequest, ChatResponse, IAssistantService, IAssistantModel,
                                          IAssistantConversation, ModelStopReason, ModelTurn, ToolCall, ToolResult,
                                          IAssistantTool, AssistantNotConfiguredException,
                                          AssistantUnavailableException, ToolInputException
  Assistant/ToolInput.cs                  strict JsonElement readers shared by tools
  Assistant/Tools/GetOrderStatusTool.cs, Assistant/Tools/ListMyOrdersTool.cs
  Assistant/ToolExecutor.cs
  Assistant/AssistantService.cs, Assistant/AssistantPrompt.cs, Assistant/AssistantValidators.cs
  DependencyInjection.cs                  (modify: register tools, executor, service)
src/OrderManagement.Infrastructure/
  Persistence/Repositories/OrderRepository.cs  (modify)
  Assistant/AssistantOptions.cs, Assistant/AnthropicAssistantModel.cs
  DependencyInjection.cs                  (modify)
src/OrderManagement.Api/
  Controllers/AssistantController.cs
  Infrastructure/GlobalExceptionHandler.cs (modify: two 503 mappings)
  appsettings.json                        (modify: "Assistant" section)
tests/OrderManagement.UnitTests/
  Fakes/InMemoryOrderRepository.cs (modify), Fakes/FakeAssistantModel.cs
  Application/Assistant/*Tests.cs, Application/OrderServiceTests.cs (modify)
  Infrastructure/AnthropicAssistantModelTests.cs
tests/OrderManagement.IntegrationTests/
  ScriptedAssistantModel.cs, ApiFactory.cs (modify), Api/AssistantEndpointsTests.cs,
  Persistence/PersistenceTests.cs (modify)
.env.example, README.md, CLAUDE.md        (modify)
```

---

### Task 1: Look up an order by its number, with the ownership rule

**Files:**
- Modify: `src/OrderManagement.Application/Orders/OrderContracts.cs`, `Orders/OrderService.cs`, `src/OrderManagement.Infrastructure/Persistence/Repositories/OrderRepository.cs`, `tests/OrderManagement.UnitTests/Fakes/InMemoryOrderRepository.cs`
- Test: `tests/OrderManagement.UnitTests/Application/OrderServiceTests.cs`, `tests/OrderManagement.IntegrationTests/Persistence/PersistenceTests.cs`

**Interfaces:**
- Produces:
  - `IOrderRepository.GetByOrderNumberAsync(string orderNumber, CancellationToken ct) → Task<Order?>`, which includes `OrderItems`.
  - `IOrderService.GetByNumberAsync(string orderNumber, CancellationToken ct) → Task<OrderResponse>`. It reuses the existing ownership check in `LoadAccessibleAsync`: refactor that check into a private method taking the loaded `Order?` and the identifier for the exception message, so the GUID and number paths share one rule. A missing order or another customer's order throws `NotFoundException("Order", …)`. Since `NotFoundException` takes a `Guid`, use `Guid.Empty` on the number path, so the message never echoes the number.

- [ ] **Step 1: Write the failing tests**

```csharp
// OrderServiceTests
[Fact] public async Task GetByNumber_returns_owners_order()
[Fact] public async Task GetByNumber_other_customers_order_throws_not_found()
[Fact] public async Task GetByNumber_missing_number_throws_not_found()   // same exception type and message as the line above
[Fact] public async Task GetByNumber_admin_gets_any_order()
// PersistenceTests
[Fact] public async Task GetByOrderNumber_includes_items()
```

- [ ] **Step 2:** Run `dotnet test tests/OrderManagement.UnitTests --filter "FullyQualifiedName~GetByNumber"` and expect FAIL to compile.
- [ ] **Step 3:** Implement. The EF query uses an exact equality match on `OrderNumber`; the stored numbers are upper-case.
- [ ] **Step 4:** Run the unit filter, plus `DOCKER_API_VERSION=1.41 dotnet test tests/OrderManagement.IntegrationTests --filter "FullyQualifiedName~GetByOrderNumber"`. Expect PASS.
- [ ] **Step 5:** Commit: `Add order lookup by order number`

---

### Task 2: Assistant contracts, strict tool input and the two tools

**Files:**
- Create: `src/OrderManagement.Application/Assistant/AssistantContracts.cs`, `Assistant/ToolInput.cs`, `Assistant/Tools/GetOrderStatusTool.cs`, `Assistant/Tools/ListMyOrdersTool.cs`
- Test: `tests/OrderManagement.UnitTests/Application/Assistant/ToolInputTests.cs`, `GetOrderStatusToolTests.cs`, `ListMyOrdersToolTests.cs`

**Interfaces:**
- Produces: every type in spec §3, exactly as written there, plus:
  - `class ToolInputException(string reason) : Exception`.
  - `class AssistantNotConfiguredException() : Exception` and `class AssistantUnavailableException(Exception inner) : Exception`.
  - `static class ToolInput`:
    - `void RequireObject(JsonElement input, params string[] allowedProperties)` throws if the input is not an object or has any other property.
    - `string RequiredString(JsonElement input, string name)`.
    - `TEnum? OptionalEnum<TEnum>(JsonElement input, string name) where TEnum : struct, Enum` takes the exact case-sensitive name and rejects numbers.
  - `GetOrderStatusTool(IOrderService)`, Name `get_order_status`. It returns `record OrderStatusResult(string OrderNumber, OrderStatus Status, DateTime CreatedAt, DateTime UpdatedAt, decimal TotalAmount, IReadOnlyList<OrderStatusItem> Items)` and `record OrderStatusItem(string ProductName, int Quantity)`.
  - `ListMyOrdersTool(IOrderService)`, Name `list_my_orders`. It returns `record MyOrdersResult(IReadOnlyList<MyOrderSummary> Orders, int TotalCount)` and `record MyOrderSummary(string OrderNumber, OrderStatus Status, DateTime CreatedAt, decimal TotalAmount)`.
  - The tool `InputSchema` values are JSON Schema objects with `additionalProperties: false`. `get_order_status` sets `pattern` to the order-number regex and makes `orderNumber` required. `list_my_orders` sets `status` to an enum of the six names. Tool descriptions say the tool only covers the signed-in customer's own orders and what it returns.

- [ ] **Step 1: Write the failing tests** (they use `FakeCurrentUser` and the existing in-memory fakes, with a real `OrderService`)

```csharp
[Fact] public void RequireObject_rejects_non_object_inputs()          // null, "x", [] → ToolInputException   (Review Focus 2)
[Fact] public void RequireObject_rejects_unknown_property()            // {"orderNumber":"…","customerId":"…"} → throws, reason names "customerId"
[Fact] public void OptionalEnum_rejects_wrong_case_and_numbers()       // "pending", 1 → throws
[Fact] public async Task GetOrderStatus_normalizes_input()             // " ord-20261023-ab12cd " finds the order (Review Focus 1)
[Fact] public async Task GetOrderStatus_rejects_malformed_number()     // "123" → ToolInputException
[Fact] public async Task GetOrderStatus_other_customers_order_throws_not_found()
[Fact] public async Task GetOrderStatus_result_contains_only_restricted_fields() // serialize the result: no Id/CustomerId/ProductId/UnitPrice/LineTotal/ShippingAddress keys, no GUID substring
[Fact] public async Task ListMyOrders_filters_by_status_and_caps_at_10()
[Fact] public async Task ListMyOrders_with_no_orders_returns_empty()  // Orders empty, TotalCount 0 (Review Focus 4)
```

- [ ] **Step 2:** Run `--filter "FullyQualifiedName~Assistant"` and expect FAIL to compile.
- [ ] **Step 3:** Implement. Tools call `ToolInput` first, then the service. `ListMyOrdersTool` calls `ListMineAsync(new OrderListQuery { Status = s, Page = 1, PageSize = 10 })`.
- [ ] **Step 4:** Run it again and expect PASS.
- [ ] **Step 5:** Commit: `Add assistant contracts and order tools`

---

### Task 3: `ToolExecutor`, the gate

**Files:**
- Create: `src/OrderManagement.Application/Assistant/ToolExecutor.cs`
- Test: `tests/OrderManagement.UnitTests/Application/Assistant/ToolExecutorTests.cs`

**Interfaces:**
- Consumes: `IAssistantTool`, `ToolCall`, `ToolResult` and the exceptions from Task 2.
- Produces: `ToolExecutor(IEnumerable<IAssistantTool> tools, ILogger<ToolExecutor> logger)` with:
  - `IReadOnlyList<IAssistantTool> Tools`
  - `Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken ct)`

  It follows spec §4 steps 1–6 in order. Results are serialized with `JsonSerializerDefaults.Web` plus `JsonStringEnumConverter`. It logs `Assistant tool {Tool} {Outcome} in {ElapsedMs}ms` with outcome `ok`, `unknown_tool`, `invalid_input`, `not_found` or `failed`. Registering two tools with the same name throws at construction.

- [ ] **Step 1: Write the failing tests** (they use small stub tools inside the test file)

```csharp
[Fact] public async Task Unknown_tool_is_not_executed()             // "delete_order" → ("Unknown tool.", IsError); no stub ran
[Fact] public async Task Tool_input_exception_becomes_invalid_arguments() // Content starts with "Invalid arguments: ", IsError
[Fact] public async Task Not_found_becomes_order_not_found()        // Content == "Order not found.", IsError
[Fact] public async Task Unexpected_exception_becomes_tool_failed_and_is_logged() // Content == "Tool failed."; logger saw Error
[Fact] public async Task Cancellation_propagates()                  // stub throws OperationCanceledException → Should.ThrowAsync (Review Focus 3)
[Fact] public async Task Success_serializes_camelcase_with_string_enums() // {"orderNumber":…,"status":"Pending"}; CallId echoed
[Fact] public async Task Non_object_input_is_invalid_arguments()    // through the real GetOrderStatusTool (Review Focus 2)
```

- [ ] **Step 2:** Run `--filter "FullyQualifiedName~ToolExecutor"` and expect FAIL.
- [ ] **Step 3:** Implement. Catch `OperationCanceledException` first and rethrow it.
- [ ] **Step 4:** Run it again and expect PASS.
- [ ] **Step 5:** Commit: `Add assistant tool executor gate`

---

### Task 4: `AssistantService` loop, prompt, validator and registration

**Files:**
- Create: `src/OrderManagement.Application/Assistant/AssistantService.cs`, `AssistantPrompt.cs`, `AssistantValidators.cs`, `tests/OrderManagement.UnitTests/Fakes/FakeAssistantModel.cs`
- Modify: `src/OrderManagement.Application/DependencyInjection.cs`
- Test: `tests/OrderManagement.UnitTests/Application/Assistant/AssistantServiceTests.cs`, `ChatRequestValidatorTests.cs`

**Interfaces:**
- Consumes: `ToolExecutor`, `IAssistantModel`.
- Produces:
  - `AssistantService(IAssistantModel model, ToolExecutor executor, int maxToolRounds) : IAssistantService`. It is registered through a factory reading `maxToolRounds` from configuration in Task 5. Until then, give it a constructor default of `5`.
  - `static class AssistantPrompt { public const string System = "…"; }`, using the spec §6 wording as plain sentences.
  - `ChatRequestValidator`.
  - `AddApplication()` registers both tools as `IAssistantTool` (scoped), `ToolExecutor` (scoped) and `IAssistantService` (scoped).
  - `FakeAssistantModel` (unit tests): a queue of `ModelTurn`s; it records the system prompt, the tool names, the user message and every `ToolResult` batch.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public async Task Direct_answer_returns_text()                         // EndTurn " hi " → "hi"
[Fact] public async Task One_tool_round_then_answer()                         // fake saw one batch with the executor's result
[Fact] public async Task Two_calls_in_one_turn_are_returned_as_one_batch_in_order()
[Fact] public async Task Exceeding_round_limit_returns_fallback()             // 6 ToolUse turns, limit 5 → Fallback; executor ran 5 rounds
[Fact] public async Task Refusal_returns_refusal_text()
[Fact] public async Task Empty_text_or_max_tokens_returns_fallback()
[Fact] public async Task Sends_fixed_prompt_and_registered_tools()            // names == ["get_order_status","list_my_orders"]
[Theory, InlineData(""), InlineData("   ")] public void Blank_message_rejected(string m)   // (Review Focus 5)
[Fact] public void Message_of_2000_accepted_and_2001_rejected()                // (Review Focus 5)
```

- [ ] **Step 2:** Run `--filter "FullyQualifiedName~Assistant"` and expect FAIL.
- [ ] **Step 3:** Implement following the spec §6 pseudocode exactly.
- [ ] **Step 4:** Run the full unit suite and expect PASS.
- [ ] **Step 5:** Commit: `Add assistant service loop`

---

### Task 5: Anthropic adapter and options

**Files:**
- Create: `src/OrderManagement.Infrastructure/Assistant/AssistantOptions.cs`, `AnthropicAssistantModel.cs`
- Modify: `src/OrderManagement.Infrastructure/DependencyInjection.cs`, `OrderManagement.Infrastructure.csproj`, `Directory.Packages.props`, `src/OrderManagement.Api/appsettings.json`
- Test: `tests/OrderManagement.UnitTests/Infrastructure/AnthropicAssistantModelTests.cs`

**Interfaces:**
- Consumes: `IAssistantModel`, `IAssistantConversation`, `ModelTurn`, `ToolCall`, `ToolResult`, `IAssistantTool` and the exceptions from Task 2.
- Produces:
  - `AssistantOptions`, section `"Assistant"`: `Model`, `MaxTokens`, `Effort`, `MaxToolRounds`, `EnableFallbacks`, plus a static `Validate` implementing the rules in Global Constraints. `ApiKey` is NOT bound from this section; it is read from `ANTHROPIC_API_KEY`.
  - `AnthropicAssistantModel(IOptions<AssistantOptions>, IConfiguration) : IAssistantModel`, registered as a singleton.
    - The `AnthropicClient` is created lazily, only when the key is non-empty, with a 60 second timeout.
    - `StartAsync` throws `AssistantNotConfiguredException` when the key is empty.
  - `AddInfrastructure` binds and validates `AssistantOptions` (`ValidateOnStart`), registers `IAssistantModel`, and re-registers `IAssistantService` with `MaxToolRounds` taken from the options.
  - `appsettings.json` gets the `"Assistant"` section with the defaults from Global Constraints.

**SDK usage** (the C# reference is in the `claude-api` skill, `csharp/claude-api/README.md` and `tool-use.md`; read it before writing):
- Use the **beta** endpoint, `client.Beta.Messages.Create` with `Anthropic.Models.Beta.Messages` types, for every request.
  - When `EnableFallbacks` is true, set `Betas = ["server-side-fallback-2026-07-01"]` and fallbacks `"default"`.
  - If 13.1.0 cannot express the `"default"` form, use the array form `[new(Model.ClaudeOpus5)]` with beta `server-side-fallback-2026-06-01`, and note this in the report.
- **Tools:** each `IAssistantTool` becomes a beta tool with `Strict = true`, its `Name` and `Description`, and an input schema built from `InputSchema` (properties, required and `additionalProperties: false`). If the typed `InputSchema` can't carry `additionalProperties`, use the SDK's additional-properties mechanism.
- **Request settings:** `ToolChoice` auto; `OutputConfig.Effort` from the options; no `Thinking` field; `MaxTokens` from the options.
- **Echoing the assistant turn:** rebuild each content block as its `*Param` counterpart, as in the `tool-use.md` section "Converting response content to the follow-up assistant message". Keep thinking blocks with their `Signature`. Then append one user message holding every `ToolResultBlockParam` (`ToolUseID`, `Content`, `IsError`).
- **Mapping:** `StopReason` maps `end_turn`, `tool_use`, `refusal` and `max_tokens` to `ModelStopReason`, and anything else to `Other`. `Text` is the text blocks joined with `"\n"`. Each `tool_use` block becomes `ToolCall(ID, Name, Input as JsonElement)`.
- **Errors:** wrap `AnthropicRateLimitException`, `Anthropic5xxException`, `AnthropicIOException` and `TaskCanceledException` from a timeout (not from the caller's token) in `AssistantUnavailableException`. Let other `AnthropicApiException` errors propagate.

- [ ] **Step 1: Write the failing tests.** Stub the API with a `System.Net.HttpListener` on a free localhost port and point the client at it (`BaseUrl`). The listener returns canned Messages-API JSON and records each request body.

```csharp
[Fact] public async Task Start_without_key_throws_not_configured()
[Fact] public async Task Maps_tool_use_turn_to_tool_calls()                    // stop_reason tool_use, two tool_use blocks → two ToolCalls, Input JSON intact
[Fact] public async Task Continue_echoes_thinking_with_signature_and_sends_all_results_in_one_user_message()
[Fact] public async Task Request_uses_options_strict_tools_and_fallbacks()      // body has model, max_tokens, output_config.effort "low", tools[].strict true, additionalProperties false, fallbacks
[Fact] public async Task Fallbacks_omitted_when_disabled()
[Fact] public async Task Server_error_after_retries_throws_unavailable()       // listener returns 529/500 every time
[Fact] public void Options_validation_rejects_out_of_range_values()            // MaxToolRounds 0/11, MaxTokens 0, Effort "turbo"
```

- [ ] **Step 2:** Run `--filter "FullyQualifiedName~AnthropicAssistantModel"` and expect FAIL.
- [ ] **Step 3:** Add `<PackageVersion Include="Anthropic" Version="13.1.0" />` centrally and a reference from Infrastructure. Implement. Fix type names from the compiler output, not by reflection probes.
- [ ] **Step 4:** Run the filter, then `dotnet build` (0 warnings) and the full unit suite. Expect PASS.
- [ ] **Step 5:** Commit: `Add Anthropic assistant model adapter`

---

### Task 6: Endpoint, 503 mapping, integration tests and docs

**Files:**
- Create: `src/OrderManagement.Api/Controllers/AssistantController.cs`, `tests/OrderManagement.IntegrationTests/ScriptedAssistantModel.cs`, `tests/OrderManagement.IntegrationTests/Api/AssistantEndpointsTests.cs`
- Modify: `src/OrderManagement.Api/Infrastructure/GlobalExceptionHandler.cs`, `tests/OrderManagement.IntegrationTests/ApiFactory.cs`, `.env.example`, `README.md`, `CLAUDE.md`

**Interfaces:**
- Consumes: `IAssistantService`, `IAssistantModel`, `AuthPolicies.CustomerOnly`, and the existing harness (`IntegrationTestBase`, `TestClient`, `Problem.AssertAsync`).
- Produces:
  - **`AssistantController`:** route `api/v1/assistant`, `[Authorize(AuthPolicies.CustomerOnly)]`, `POST chat` → 200 `ChatResponse`.
  - **`GlobalExceptionHandler`:** `AssistantNotConfiguredException` → 503 with `"Assistant is not configured."`, and `AssistantUnavailableException` → 503 with `"Assistant is temporarily unavailable."`.
  - **`ScriptedAssistantModel : IAssistantModel`**, a singleton in `ApiFactory`, reset in `IntegrationTestBase.InitializeAsync`:
    - `Script(params Func<IReadOnlyList<ToolResult>?, ModelTurn>[] turns)`
    - `IReadOnlyList<IReadOnlyList<ToolResult>> ReceivedResults`
    - It replaces `IAssistantModel` through `ConfigureTestServices`.
  - **`ApiFactory`:** adds `ANTHROPIC_API_KEY = ""` to its in-memory configuration. It exposes `ScriptedAssistantModel Assistant`, plus `WebApplicationFactory<Program> WithRealAssistantModel()`, which removes the scripted registration so the 503 path can be tested.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public async Task Anonymous_gets_401()
[Fact] public async Task Admin_gets_403()
[Fact] public async Task Customer_gets_reply()                                  // script: EndTurn "Hello" → 200 {reply:"Hello"}
[Fact] public async Task Customer_cannot_see_another_customers_order()          // A orders; B asks; script calls get_order_status(A's number)
    // → ReceivedResults[0] == [("Order not found.", IsError: true)]; response body and results contain neither A's
    //   order number status details nor product names
[Fact] public async Task Own_order_returns_restricted_result()                  // result JSON has status "Pending" and A's number, no GUIDs
[Fact] public async Task Blank_message_returns_400()
[Fact] public async Task Not_configured_returns_503()                           // WithRealAssistantModel(), key "" → 503 problem "Assistant is not configured."
```

- [ ] **Step 2:** Run `DOCKER_API_VERSION=1.41 dotnet test tests/OrderManagement.IntegrationTests --filter "FullyQualifiedName~AssistantEndpoints"` and expect FAIL.
- [ ] **Step 3:** Implement the controller, the handler mappings and the harness additions.
- [ ] **Step 4:** Update the docs.
  - **`.env.example`:** add `ANTHROPIC_API_KEY=` with the comment "optional; the assistant endpoint returns 503 without it".
  - **README:** add an "Assistant" section with the endpoint, the key, an example request and response, the two tools, the security model in two sentences, and a manual smoke-test command using a real key.
  - **CLAUDE.md:** add an "Assistant" section covering:
    - where the code lives
    - tools take no identity from model input and always go through `ToolExecutor`
    - how to add a tool: implement `IAssistantTool`, register it in `AddApplication`, add a restricted DTO and tests
    - tests use `ScriptedAssistantModel`, never the real API
- [ ] **Step 5:** Run the full `DOCKER_API_VERSION=1.41 dotnet test` and expect everything to pass with 0 warnings. Commit: `Add assistant chat endpoint`
