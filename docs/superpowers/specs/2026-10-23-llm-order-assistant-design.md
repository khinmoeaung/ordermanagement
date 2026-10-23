# LLM Order Assistant (Tool-Calling Executor): Design

**Date:** 2026-10-23
**Status:** Awaiting review
**Builds on:** `2026-10-23-ordermanagement-core-api-design.md` (the Core API). Branch `feature/llm-assistant`, created from `feature/core-api-init`.

## 1. Purpose and scope

This adds an LLM assistant to the Core API. A signed-in customer asks a question in plain language, such as "What is the status of ORD-20261023-AB12CD?". Claude chooses a tool, the backend checks and runs that tool, and Claude writes the answer from the restricted result.

The point of the portfolio piece is the security rule:

> An order identifier proposed by the LLM is untrusted input. The LLM choosing an order number never implies the caller may see that order. The backend always authorizes against the **authenticated user from the JWT**, through the same ownership rule the REST API uses.

**In scope:**
- one endpoint for customers only: one question per request, nothing stored
- two read-only tools: `get_order_status` and `list_my_orders`
- the Anthropic Claude API, through the official `Anthropic` .NET SDK

**Out of scope:**
- write or action tools such as cancelling orders
- admin use of the assistant, and role-dependent tool sets
- conversation history, whether sent by the client or stored on the server
- streaming responses
- rate limiting
- product tools
- other LLM providers

**Success criteria:**
- With `ANTHROPIC_API_KEY` set, a customer can ask about their own orders through `POST /api/v1/assistant/chat` and get a correct answer.
- Asking about another customer's order number reveals nothing about that order. An integration test proves this using a scripted fake model.
- The whole automated suite runs without an API key.

## 2. Components

```
Api/Controllers/AssistantController.cs   POST /api/v1/assistant/chat   [Authorize(AuthPolicies.CustomerOnly)]

Application/Assistant/
  AssistantContracts.cs   ChatRequest, ChatResponse, IAssistantService,
                          IAssistantModel, IAssistantConversation, ModelTurn, ToolCall, ToolResult, ModelStopReason,
                          AssistantUnavailableException, AssistantNotConfiguredException, ToolInputException
  AssistantService.cs     the tool-use loop
  ToolExecutor.cs         the gate: allowlist → strict input validation → execute → result
  IAssistantTool.cs       one tool: Name, Description, InputSchema, ExecuteAsync
  Tools/GetOrderStatusTool.cs
  Tools/ListMyOrdersTool.cs
  AssistantValidators.cs  ChatRequestValidator
  AssistantPrompt.cs      the fixed system prompt

Application/Orders/   (additions)
  IOrderRepository.GetByOrderNumberAsync(string orderNumber, CancellationToken ct)   includes OrderItems
  IOrderService.GetByNumberAsync(string orderNumber, CancellationToken ct)         same ownership rule as GetAsync

Infrastructure/Assistant/
  AnthropicAssistantModel.cs   IAssistantModel built on the Anthropic SDK; keeps the SDK message list inside the conversation
  AssistantOptions.cs          the "Assistant" configuration section
```

- **Dependencies are unchanged:** Application defines `IAssistantModel`, and Infrastructure implements it. No Anthropic SDK type appears in Application or Api.
- **The tools receive no identity from the LLM:** they call `IOrderService`, which reads the caller through `ICurrentUser` (the JWT).
- **No schema change:** `orders.order_number` already has a unique index.

## 3. Contracts

```csharp
record ChatRequest(string Message);
record ChatResponse(string Reply);
interface IAssistantService { Task<ChatResponse> ChatAsync(ChatRequest request, CancellationToken ct); }

interface IAssistantModel {
    Task<(IAssistantConversation Conversation, ModelTurn FirstTurn)> StartAsync(
        string systemPrompt, IReadOnlyList<IAssistantTool> tools, string userMessage, CancellationToken ct);
}
interface IAssistantConversation {
    Task<ModelTurn> ContinueAsync(IReadOnlyList<ToolResult> results, CancellationToken ct);
}
enum ModelStopReason { EndTurn, ToolUse, Refusal, MaxTokens, Other }
record ModelTurn(ModelStopReason StopReason, string Text, IReadOnlyList<ToolCall> ToolCalls);
record ToolCall(string Id, string Name, JsonElement Input);
record ToolResult(string CallId, string Content, bool IsError);

interface IAssistantTool {
    string Name { get; }
    string Description { get; }
    JsonElement InputSchema { get; }   // JSON Schema, additionalProperties: false
    Task<object> ExecuteAsync(JsonElement input, CancellationToken ct);   // returns the restricted DTO; throws on bad input
}
```

## 4. The gate: `ToolExecutor.ExecuteAsync(ToolCall call, CancellationToken ct) → ToolResult`

These steps run in order. Each one returns as soon as it fails.

1. **Allowlist.** The tool must be registered under exactly `call.Name`. If not, the result is `ToolResult(call.Id, "Unknown tool.", IsError: true)` and nothing executes.
2. **Strict input validation**, done by the tool and wrapped by the executor. Input must be a JSON object containing only the documented properties, with the right types and formats. Any violation, including an unknown extra property, throws `ToolInputException`, and the result is `"Invalid arguments: <reason>"` with `IsError` set. The schema sent to Claude also uses `strict: true`, but the backend never relies on it.
3. **Execute as the authenticated user.** The tool calls `IOrderService`, which enforces ownership through `ICurrentUser`. No tool accepts a user ID, customer ID or role.
4. **Restricted output.** The DTO is serialized as camelCase JSON, enums as strings, and that becomes `Content`.
5. **Errors:**
   - `NotFoundException` becomes `"Order not found."` with `IsError`. A missing order and another customer's order produce the identical result.
   - Any other exception becomes `"Tool failed."` with `IsError`. The exception is logged at Error.
6. **Logging:** each call logs the tool name, the outcome (`ok`, `unknown_tool`, `invalid_input`, `not_found` or `failed`) and the duration. Argument values and message text are never logged.

## 5. Tools

| Name | Input schema | Calls | Output DTO |
|---|---|---|---|
| `get_order_status` | `{ "orderNumber": string }`, required, matching `^ORD-\d{8}-[A-Z0-9]{6}$`. The input is trimmed and upper-cased before the check. | `IOrderService.GetByNumberAsync` | `{ orderNumber, status, createdAt, updatedAt, totalAmount, items: [{ productName, quantity }] }` |
| `list_my_orders` | `{ "status"?: "Pending"\|"Confirmed"\|"Shipped"\|"Delivered"\|"Completed"\|"Cancelled" }` | `IOrderService.ListMineAsync` with `Page = 1`, `PageSize = 10` | `{ orders: [{ orderNumber, status, createdAt, totalAmount }], totalCount }` |

- **Never sent to the model:** order, customer and product IDs; unit prices and line totals; the shipping address; any other customer's data.
- **Tool descriptions** say what the tool returns, and that it only covers the signed-in customer's own orders.

## 6. The loop: `AssistantService.ChatAsync`

```
(conversation, turn) = model.StartAsync(AssistantPrompt.System, tools, request.Message)
rounds = 0
while turn.StopReason == ToolUse:
    rounds++
    if rounds > MaxToolRounds: return Fallback
    results = [await executor.ExecuteAsync(call) for call in turn.ToolCalls]   // sequential, one batch
    turn = await conversation.ContinueAsync(results)
EndTurn   → return turn.Text (trimmed). If it's empty, return Fallback.
Refusal   → return "Sorry, I can't help with that request."
MaxTokens / Other → return Fallback
```

- `Fallback` is `"Sorry, I couldn't complete that request. Please try again."`
- `MaxToolRounds` is configurable and defaults to 5.
- All results for one turn go back in one `ContinueAsync` call.

**System prompt (`AssistantPrompt.System`), fixed text:** you are the order assistant for the signed-in customer of this shop; answer only questions about the customer's own orders; use the tools for every order fact and never guess or invent order data; if a tool reports an order was not found, say you could not find that order on their account; politely decline unrelated requests; answer briefly.

## 7. Anthropic adapter (`AnthropicAssistantModel`)

- **Package:** the official `Anthropic` NuGet package. The version is pinned during planning, after checking support for the `fallbacks` parameter and the `strict` tool definitions.
- **Request settings:**
  - model from `Assistant:Model`, default `claude-opus-5-5`
  - `max_tokens` from `Assistant:MaxTokens`, default 16000; non-streaming
  - `output_config.effort` from `Assistant:Effort`, default `low`
  - thinking: not configured (always on for this model)
  - `tool_choice: auto`
  - tools sent with `strict: true`
- **Server-side fallback:** when `Assistant:EnableFallbacks` is true (the default), requests use the beta `server-side-fallback-2026-07-01` with `fallbacks: "default"`.
- **Conversation state:** the adapter keeps the SDK message list. It appends the assistant's full `content` unchanged, including thinking blocks, followed by one user message holding every `tool_result` block, with `is_error` set from `ToolResult`.
- **Mapping to `ModelTurn`:** `stop_reason` maps to `ModelStopReason`; the text blocks are joined to form `Text`; and the `tool_use` blocks become `ToolCalls` with their parsed JSON input.
- **Client configuration:** the API key comes from `ANTHROPIC_API_KEY`; the SDK keeps its default of 2 retries; the timeout is 60 seconds.
- **Errors:** connection errors, timeouts, 429 and 5xx (after retries) throw `AssistantUnavailableException`. Other API errors (400, 401, 403, 404) propagate unchanged and become a 500.
- **When the key is not configured:** the adapter throws `AssistantNotConfiguredException` on `StartAsync`, so the app still starts without a key.

## 8. HTTP behaviour

| Case | Status |
|---|---|
| Success | 200 `{ "reply": "..." }` |
| `Message` empty or whitespace, or longer than 2000 characters | 400 ValidationProblemDetails |
| No token, or an invalid token | 401 |
| Admin token | 403 |
| `AssistantNotConfiguredException` | 503 ProblemDetails "Assistant is not configured." |
| `AssistantUnavailableException` | 503 ProblemDetails "Assistant is temporarily unavailable." |
| Other errors | 500, as the existing `GlobalExceptionHandler` does today |

`GlobalExceptionHandler` gains the two 503 mappings. Customer messages and model replies are never logged.

## 9. Configuration

```json
"Assistant": { "Model": "claude-opus-5-5", "MaxTokens": 16000, "Effort": "low", "MaxToolRounds": 5, "EnableFallbacks": true }
```

- **The API key** comes from the environment variable `ANTHROPIC_API_KEY`: `.env` in Docker, user-secrets or the environment locally. `.env.example` gets an empty `ANTHROPIC_API_KEY=` line.
- **Startup checks:** `AssistantOptions` checks at startup that `MaxTokens` > 0, `MaxToolRounds` is between 1 and 10, and `Effort` is one of `low`, `medium`, `high`, `xhigh` or `max`. A missing key is not a startup error (see §7).

## 10. Testing

**Unit tests (UnitTests):**
- **`ToolExecutor`:**
  - an unknown tool doesn't execute
  - an extra property is rejected
  - a missing or ill-typed property is rejected
  - a malformed order number is rejected
  - `NotFoundException` becomes `"Order not found."`
  - an unexpected exception becomes `"Tool failed."` and is logged
  - a valid call's content contains only the DTO fields, and the test asserts that no GUID or address appears
- **The tools:** they pass only the normalized order number or the status to `IOrderService`, and map to the restricted DTOs.
- **`AssistantService`, with a scripted `FakeAssistantModel`:**
  - a direct answer
  - one tool round
  - two tool calls in one turn, returned as one batch in order
  - the round limit giving `Fallback`
  - a refusal giving the refusal text
  - an empty text giving `Fallback`
- **`OrderService.GetByNumberAsync`:** the owner gets the order; another customer and a missing number both get `NotFoundException`; an admin gets any order.
- **Validators:** `ChatRequestValidator`.

**Integration tests (IntegrationTests, Api collection):**
- `ApiFactory` replaces `IAssistantModel` with a scripted fake the test can program, and which records the `ToolResult`s it receives.
- **Access:** anonymous gets 401, an admin gets 403, and a customer gets 200 with the fake's final text.
- **The cross-customer test:** customer A places an order. Customer B asks; the fake calls `get_order_status` with A's order number. The test asserts that the fake received `"Order not found."` with `IsError`, and that neither the response body nor any `ToolResult` contains A's order data.
- **The own-order test:** customer A asks about their own order, and the fake receives a `ToolResult` holding the restricted DTO with the right status.
- **Errors:** an empty message gets 400; with a real `AnthropicAssistantModel` and no key, the response is 503 "Assistant is not configured."

There are no live Anthropic calls in the automated suite. The README documents a manual smoke test with a real key.

## 11. Documentation

- **README:** an "Assistant" section covering the endpoint, `ANTHROPIC_API_KEY`, an example request, and the security model in two sentences.
- **CLAUDE.md:** where the assistant lives; the rule that tools take no identity from the LLM and go through `ToolExecutor`; and how to add a tool (implement `IAssistantTool`, register it, add a restricted DTO and tests).
