# OrderManagement Core API: Design

**Date:** 2026-10-23
**Status:** Awaiting review

## 1. Purpose and scope

A portfolio project: a backend REST API for order management, built on .NET 10. It should show a clean layered monolith, JWT authentication with refresh tokens, a domain with real business rules, and thorough automated tests.

**In scope:** the layered solution, authentication (register, login, refresh, logout), Admin and Customer roles, product and customer management, orders with a status workflow and stock control, MySQL with EF Core, database-first (§2a), Swagger, Serilog, unit and integration tests, and Docker / Docker Compose.

**Out of scope (later projects):** GitHub Actions and GHCR, LLM and AI-agent integrations, email and notifications (so no email confirmation or password reset), file storage, webhooks, a Staff role, payments, and any frontend.

**Success criteria:**
- `docker compose up --build` starts the API and MySQL. Swagger at `http://localhost:8080/swagger` lets you register, log in, authorize with the token and run the whole order lifecycle.
- `dotnet test` passes all unit and integration tests. The integration tests need Docker.
- Every business rule in this document is covered by at least one test.

## 2. Solution layout

```
OrderManagement.slnx               (the .NET 10 default solution format)
Directory.Build.props      net10.0, Nullable enable, TreatWarningsAsErrors, ImplicitUsings
.editorconfig  .gitignore  .env.example  Dockerfile  docker-compose.yml  README.md  CLAUDE.md
database/migrations/       numbered SQL scripts: the schema's source of truth (§2a)
scripts/scaffold.ps1       regenerates the EF model from the database
src/
  OrderManagement.Domain/          no project or package dependencies
  OrderManagement.Application/     -> Domain
  OrderManagement.Infrastructure/  -> Application, Domain
  OrderManagement.Api/             -> Application, Infrastructure (Infrastructure is referenced only to register its services)
tests/
  OrderManagement.UnitTests/         -> Domain, Application, Infrastructure (token service only)
  OrderManagement.IntegrationTests/  -> Api
```

What each layer contains:

- **Domain:** entities in `Entities/`. Each is a generated `partial` class (scaffolded from the database, no attributes) plus a hand-written `<Entity>.Behavior.cs` partial with the factory and rule methods. Also enums (`UserRole`, `OrderStatus`), and domain exceptions (`DomainException`, `NotFoundException`, `ConflictException`). No EF Core or ASP.NET Core references.
- **Application:** service interfaces and implementations (`IAuthService`, `IProductService`, `ICustomerService`, `IOrderService`), request/response classes, FluentValidation validators, and hand-written mapping extension methods. It also defines the interfaces that Infrastructure implements: the repositories, `IUnitOfWork`, `ITokenService`, `IPasswordHasher` and `ICurrentUser`. The current time comes from .NET's built-in `TimeProvider`. Each layer provides an `AddApplication()` / `AddInfrastructure()` method to register its services.
- **Infrastructure:** the generated `AppDbContext` plus a hand-written `AppDbContext.Partial.cs` (which implements `IUnitOfWork` and adds the concurrency token in `OnModelCreatingPartial`), the embedded SQL scripts and the DbUp runner, the customized EF scaffolding templates, a repository for each aggregate, the JWT token service, a password hasher wrapping ASP.NET Core's `PasswordHasher<User>` (not the full Identity framework), and the database seeder.
- **Api:** controllers, the global exception handler, JWT bearer and authorization-policy setup, Swagger with bearer-token support, Serilog, the `/health` endpoint, and `ICurrentUser`, implemented by reading the `HttpContext` claims.

**Packages:** EF Core 10 with a MySQL provider, DbUp (`dbup-mysql` 6.1.0), FluentValidation, Microsoft.AspNetCore.Authentication.JwtBearer, Serilog.AspNetCore, Swagger UI, xUnit, a fluent assertion library, Testcontainers.MySql, and Microsoft.AspNetCore.Mvc.Testing.

**MySQL provider:** Oracle's `MySql.EntityFrameworkCore` 10.0.12. Pomelo has no EF Core 10 release (its latest is 9.0.0). Exact package versions are pinned in the implementation plan.

## 2a. Database-first workflow

The SQL schema is the source of truth. The EF Core model is generated from it, never the other way round.

- **Schema scripts:** `database/migrations/NNNN_<name>.sql`, numbered from `0001_initial_schema.sql`. A script is never edited once committed; every change is a new script. Tables and columns use `snake_case` (`order_items.unit_price`). The schema enforces what it can: primary and foreign keys (`RESTRICT`, except `order_items → orders` which is `CASCADE`), the unique keys from §3, `CHECK (price > 0)`, `CHECK (stock_quantity >= 0)`, `CHECK (quantity >= 1)`, `DECIMAL(18,2)` money, `CHAR(36)` IDs, `VARCHAR` status and role columns, `DATETIME(6)` times, InnoDB with `utf8mb4`.
- **Applying scripts:** DbUp runs the scripts (embedded resources of the Infrastructure project) in order and records each one in a `schemaversions` table, so each script runs once. It runs at startup in the `Development` and `Docker` environments, and once per integration-test run. There are no EF Core migrations.
- **Generating the model:** `scripts/scaffold.ps1` starts a throwaway `mysql:8.4` container, pipes the scripts into the container's `mysql` client in name order, and runs `dotnet ef dbcontext scaffold` with these settings:
  - generated entities go to `src/OrderManagement.Domain/Entities/` (namespace `OrderManagement.Domain.Entities`), and the `AppDbContext` goes to `src/OrderManagement.Infrastructure/Persistence/`
  - mapping uses the fluent API only (no attributes), `snake_case` names become `PascalCase`, and the DbContext gets no hard-coded connection string
  - the `schemaversions` table is excluded
- **Customized templates** (`dotnet new ef-templates`, checked into Infrastructure under `CodeTemplates/EFCore/`) make the generated code match this design:
  - `CHAR(36)` columns named `id` or ending in `_id` become `Guid`
  - `orders.status` becomes `OrderStatus` and `users.role` becomes `UserRole`, stored as strings
  - entity classes are `partial`, and every generated file starts with an `<auto-generated>` header
- **Hand-written partials** that scaffolding never overwrites:
  - `Domain/Entities/<Entity>.Behavior.cs`: the factory methods and rule methods from §3
  - `Infrastructure/Persistence/AppDbContext.Partial.cs`: marks `Product.StockQuantity` as a concurrency token in `OnModelCreatingPartial`, and implements `IUnitOfWork`
- **Convention:** generated setters are public. Application services change entities only through the behavior methods, and code review enforces this.
- **Changing the schema:** add a new SQL script, run `scripts/scaffold.ps1`, update the behavior partials and tests, then commit the script and the regenerated code together.

## 3. Domain model

All IDs are `Guid`, generated with `Guid.CreateVersion7()`. Money is `decimal(18,2)`. Timestamps are UTC and come from `TimeProvider`. Domain methods take the current time (`DateTime utcNow`) as a parameter, so the domain has no dependency on a clock.

| Entity | Fields | Rules |
|---|---|---|
| **User** | Id, Email (unique, stored lower-case), PasswordHash, Role, CreatedAt | Registering creates a `Customer` user. One `Admin` is seeded from configuration. |
| **Customer** | Id, UserId (one-to-one, unique), FullName, Phone, ShippingAddress | Created in the same transaction as the user at registration. |
| **Product** | Id, Sku (unique), Name, Description, Price, StockQuantity, IsActive, CreatedAt, UpdatedAt | `DeductStock(qty)` throws `ConflictException` if stock would go below 0. `RestoreStock(qty)` adds it back. Price must be greater than 0 and stock at least 0. Deleting sets `IsActive = false`. `StockQuantity` is an EF Core concurrency token. |
| **Order** | Id, OrderNumber, CustomerId, Status, TotalAmount, CreatedAt, UpdatedAt, Items | Created only through `Order.Create(customerId, lines, utcNow)`, which needs at least one line and calculates `TotalAmount` from the lines. OrderNumber looks like `ORD-yyyyMMdd-XXXXXX`, where `XXXXXX` is 6 random alphanumeric characters, and is unique. |
| **OrderItem** | Id, OrderId, ProductId, ProductName, UnitPrice, Quantity, LineTotal | ProductName and UnitPrice are copied from the product when the order is placed. Quantity must be at least 1. |
| **RefreshToken** | Id, UserId, TokenHash (unique), ExpiresAt, CreatedAt, RevokedAt, ReplacedByTokenHash | Active means not revoked and not expired. |

### Order status workflow

`OrderStatus`: `Pending`, `Confirmed`, `Shipped`, `Delivered`, `Completed`, `Cancelled`.

| Method | Allowed from | Result |
|---|---|---|
| `Confirm()` | Pending | Confirmed |
| `Ship()` | Confirmed | Shipped |
| `Deliver()` | Shipped | Delivered |
| `Complete()` | Delivered | Completed |
| `Cancel()` | Pending, Confirmed | Cancelled. The service restores the stock for every item. |

Any other change throws `ConflictException`. `Completed` and `Cancelled` are final. Every change sets `UpdatedAt`.

## 4. API

Base path: `/api/v1`. Request and response bodies are JSON, and enums are serialized as strings.

| Endpoint | Access | Notes |
|---|---|---|
| `POST /auth/register` | Anonymous | Takes email, password, fullName, phone and shippingAddress. Returns 201 with the tokens. A duplicate email returns 409. |
| `POST /auth/login` | Anonymous | Returns `{ accessToken, accessTokenExpiresAt, refreshToken, refreshTokenExpiresAt }`. Any bad credentials return 401. |
| `POST /auth/refresh` | Anonymous | Takes `{ refreshToken }`. Rotates the token and returns a new pair. |
| `POST /auth/logout` | Authenticated | Takes `{ refreshToken }` and revokes it. Returns 204. |
| `GET /customers/me`, `PUT /customers/me` | Customer | The caller's own profile. `PUT` updates fullName, phone and shippingAddress (email and role can't be changed). |
| `GET /customers`, `GET /customers/{id}` | Admin | Paged list, searchable by name or email. |
| `GET /products`, `GET /products/{id}` | Anonymous | Non-admins only see active products, and an inactive one returns 404. Paged, with `?search=` on name or SKU. Admins can use `?includeInactive=true`. |
| `POST /products`, `PUT /products/{id}`, `DELETE /products/{id}` | Admin | Delete deactivates the product and returns 204. A duplicate SKU returns 409. |
| `POST /orders` | Customer | Takes `{ items: [{ productId, quantity }] }`. Returns 201 with a `Location` header. |
| `GET /orders/my` | Customer | The caller's orders, paged, filterable by `status`. |
| `GET /orders` | Admin | All orders, paged, filterable by `status`, `customerId`, `from` and `to`. |
| `GET /orders/{id}` | Customer (own) or Admin | |
| `POST /orders/{id}/confirm`, `/ship`, `/deliver` | Admin | |
| `POST /orders/{id}/complete` | Customer (own) or Admin | The order must be `Delivered`. |
| `POST /orders/{id}/cancel` | Customer (own) or Admin | The order must be `Pending` or `Confirmed`. |
| `GET /health` | Anonymous | Checks the database is reachable. |

**Paging:** `page` defaults to 1. `pageSize` defaults to 20 and must be between 1 and 100. Lists return `{ items, page, pageSize, totalCount }`, sorted newest first by default (products by name).

**Ownership:** when a customer requests an order that belongs to someone else, the API returns 404, not 403. Services check ownership using `ICurrentUser`.

### Placing an order

1. Validation: at least one item, no duplicate `productId`s, and each quantity between 1 and 1000. Failures return 400.
2. Find the customer for the current user, then load all the requested products in one query. A missing or inactive product returns 404.
3. Call `product.DeductStock(qty)` for each line. Not enough stock returns 409.
4. Call `Order.Create(...)` with the copied prices.
5. `IUnitOfWork.SaveChangesAsync()` commits the order and the stock changes in one transaction. A `DbUpdateConcurrencyException` becomes a 409 telling the client to retry.
6. Return 201 with an `OrderResponse`.

Cancelling follows the same pattern: `order.Cancel()`, then `RestoreStock` for each item, then one save.

## 5. Authentication and authorization

- **Access token:** a JWT signed with HMAC-SHA256. Claims: `sub` (user ID), `email`, `role` and `jti`. It lasts 15 minutes by default. The issuer, audience, expiry and signing key are all checked, with a clock-skew allowance of 30 seconds.
- **Refresh token:** 64 random bytes, sent to the client as Base64URL. Only the SHA-256 hash is stored. It lasts 7 days by default.
- **Refresh:** if the token is active, revoke it, set `ReplacedByTokenHash`, and issue a new pair. If the token was already revoked, treat it as stolen: revoke every active token for that user and return 401. An unknown or expired token also returns 401.
- **Logout:** revokes the given refresh token if it belongs to the caller. Returns 204 even if the token was already revoked.
- **Passwords:** at least 8 characters, with at least one letter and one digit. Hashed with `PasswordHasher<User>`.
- **Login:** the same 401 response whether the email is unknown or the password is wrong.
- **Policies:** `AdminOnly` (role Admin) and `CustomerOnly` (role Customer). The endpoints open to "Customer (own) or Admin" only require authentication, and the service checks the role and ownership.

## 6. Error handling

A global `IExceptionHandler` returns RFC 9457 ProblemDetails responses, each including a `traceId`.

| Cause | Status |
|---|---|
| FluentValidation failure (a validation filter runs before each action) | 400 with `errors` per field |
| Bad credentials, or a missing, invalid, expired or revoked token | 401 |
| Wrong role for the policy | 403 |
| `NotFoundException` | 404 |
| `ConflictException`. Infrastructure turns `DbUpdateConcurrencyException` and MySQL duplicate-key errors (1062) into `ConflictException`. | 409 |
| Any other `DomainException` (an invariant that validation didn't catch) | 400 |
| Anything else | 500 with a generic message. Details are logged only. |

## 7. Configuration, startup and logging

- Typed, validated options classes (checked at startup): `JwtOptions` (Issuer, Audience, SigningKey of at least 32 bytes, AccessTokenMinutes, RefreshTokenDays) and `SeedOptions` (AdminEmail, AdminPassword). The app refuses to start if any are invalid.
- Secrets (the JWT signing key, the `ConnectionStrings:Default` connection string and the admin password) come from user-secrets in Development and from environment variables in Docker. `.env.example` documents them, and `.env` is git-ignored.
- DbUp applies any new schema scripts on startup in the `Development` and `Docker` environments (§2a).
- The seeder creates the admin user and 5 sample products if they don't already exist. Running it again changes nothing.
- Serilog writes structured logs to the console, one summary line per request. Passwords, tokens and `Authorization` headers are never logged.
- Swagger UI is enabled in the `Development` and `Docker` environments, with a bearer-token input.

## 8. Testing

Development is test-first: write a failing test, make it pass, then tidy up.

**Unit tests** (no I/O):
- Domain: every allowed and rejected status change, stock deduction and restoring, `Order.Create` rules and total calculation, and the copied prices.
- Services: run against in-memory fakes. Cover ownership (another customer's order returns `NotFoundException`), placing an order with missing, inactive or out-of-stock products, cancelling and restoring stock, refresh rotation and reuse detection, login failures, and duplicate email or SKU.
- Validators: every rule.

**Integration tests:** `WebApplicationFactory` plus a Testcontainers `mysql:8.4` container shared by the test collection, with the database reset between tests.
- Auth: register, login, refresh and logout, a reused refresh token getting 401 and revoking every token, and an expired or tampered access token getting 401.
- Authorization: for each kind of endpoint, anonymous callers get 401, the wrong role gets 403, and another customer's order returns 404.
- Order lifecycle: an admin creates a product, a customer orders it and stock goes down, a cancel restores the stock, and the full path through `Completed` works. An illegal change returns 409.
- Concurrency: two parallel orders for the last unit, where exactly one gets 201 and the other gets 409.
- Validation produces a 400 ProblemDetails response with field errors.

## 9. Docker

- `Dockerfile`: a multi-stage build that compiles with the .NET 10 SDK image and runs on the `aspnet:10.0` image as a non-root user, on port 8080.
- `docker-compose.yml`: `mysql:8.4` (utf8mb4, a named volume, a health check) and `api` (waits until MySQL is healthy, `ASPNETCORE_ENVIRONMENT=Docker`, settings from `.env`), with port 8080 exposed.
- The README documents: `docker compose up --build`, running locally with `dotnet run` plus user-secrets, `dotnet test`, running a single test with `dotnet test --filter "FullyQualifiedName~<Name>"`, and the schema-change workflow from §2a.
