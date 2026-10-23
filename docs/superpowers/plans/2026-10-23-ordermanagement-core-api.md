# OrderManagement Core API Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a .NET 10 layered-monolith REST API for order management with JWT access and refresh tokens, Admin and Customer roles, products, customers and orders with a status workflow and stock control, on MySQL 8.4.

**Architecture:** Four projects, Domain ← Application ← Infrastructure ← Api. Domain entities enforce the business rules. Application services coordinate repositories through interfaces. Infrastructure implements those interfaces with EF Core and the Oracle MySQL provider. Api holds thin controllers plus cross-cutting concerns (JWT, ProblemDetails, validation filter, Swagger, Serilog).

**Tech Stack:** .NET 10 / ASP.NET Core, EF Core 10.0.12 + MySql.EntityFrameworkCore 10.0.12, FluentValidation 12.2.0, Serilog.AspNetCore 10.0.0, Swashbuckle.AspNetCore 10.3.1, xunit.v3 4.0.2, Shouldly 4.3.0, Testcontainers.MySql 4.16.0, Docker Compose.

**Spec:** `docs/superpowers/specs/2026-10-23-ordermanagement-core-api-design.md`. Read it alongside this plan: section numbers below (§N) refer to it.

## Global Constraints

- Target `net10.0` for every project. SDK installed: 10.0.402.
- Central package versions in `Directory.Packages.props`:
  - `Microsoft.EntityFrameworkCore`, `.Relational`, `.Design` = **10.0.12** (must match the provider's dependency)
  - `MySql.EntityFrameworkCore` = **10.0.12**
  - `Microsoft.AspNetCore.Authentication.JwtBearer`, `Microsoft.AspNetCore.Mvc.Testing`, `Microsoft.Extensions.Identity.Core` = **10.0.13**
  - `Microsoft.Extensions.TimeProvider.Testing` = the latest stable **10.0.x**
  - `FluentValidation`, `FluentValidation.DependencyInjectionExtensions` = **12.2.0**
  - `Serilog.AspNetCore` = **10.0.0**
  - `Swashbuckle.AspNetCore` = **10.3.1**
  - `xunit.v3` = **4.0.2**, `xunit.runner.visualstudio` = **4.0.1**, `Microsoft.NET.Test.Sdk` = **18.11.0**
  - `Shouldly` = **4.3.0**, `Testcontainers.MySql` = **4.16.0**
- `Directory.Build.props`: `Nullable=enable`, `ImplicitUsings=enable`, `TreatWarningsAsErrors=true`, `LangVersion=latest`.
- No AutoMapper, MediatR or FluentAssertions (all commercial licenses). Map by hand with extension methods.
- Domain has no package references. It never reads the clock: the current time comes in as `DateTime utcNow`.
- IDs: `Guid.CreateVersion7()`. Money: `decimal(18,2)`. All times UTC.
- Routes are under `/api/v1`. Enums are serialized as strings. Every error response is RFC 9457 ProblemDetails with a `traceId`.
- **Commits:** one description line only, with no prefixes like `feat:` and no trailers. Work on branch `feature/core-api-init`.
- Integration tests need Docker running.

## Review Focus

1. **Malformed JSON body** (for example `{"email":`) must return a 400 ProblemDetails response, not a 500. The test is in Task 10.
2. **Email case and whitespace:** registering ` Bob@Example.com ` and then logging in as `bob@example.com` must work, and registering `BOB@example.com` afterwards must return 409. Tests are in Task 6.
3. **Cancelling an order whose product has since been deactivated** must still restore that product's stock. The test is in Task 8.
4. **Prices with more than 2 decimal places** (`19.999`) must be rejected with 400, not silently rounded. The test is in Task 5.
5. **Bad route or query values:** a non-GUID ID (`/orders/abc`) must return 404 and an unknown status (`?status=Foo`) must return 400, not 500. Tests are in Task 12.

---

## File structure

```
OrderManagement.slnx
Directory.Build.props  Directory.Packages.props  .editorconfig  .gitignore  .config/dotnet-tools.json (dotnet-ef 10.0.12)
src/OrderManagement.Domain/
  Common/      DomainException.cs, NotFoundException.cs, ConflictException.cs
  Users/       User.cs, UserRole.cs, RefreshToken.cs
  Customers/   Customer.cs
  Products/    Product.cs
  Orders/      Order.cs, OrderItem.cs, OrderStatus.cs, OrderLine.cs, OrderNumber.cs
src/OrderManagement.Application/
  Common/      PagedResult.cs, PageQuery.cs, AuthenticationFailedException.cs, ICurrentUser.cs, IUnitOfWork.cs
  Auth/        IAuthService.cs, AuthService.cs, ITokenService.cs, IPasswordHasher.cs, IUserRepository.cs, IRefreshTokenRepository.cs, AuthDtos.cs, AuthValidators.cs
  Customers/   ICustomerService.cs, CustomerService.cs, ICustomerRepository.cs, CustomerDtos.cs, CustomerValidators.cs, CustomerMappings.cs
  Products/    IProductService.cs, ProductService.cs, IProductRepository.cs, ProductDtos.cs, ProductValidators.cs, ProductMappings.cs
  Orders/      IOrderService.cs, OrderService.cs, IOrderRepository.cs, OrderDtos.cs, OrderValidators.cs, OrderMappings.cs
  DependencyInjection.cs
src/OrderManagement.Infrastructure/
  Persistence/ AppDbContext.cs, Configurations/*.cs, Migrations/, Repositories/*.cs, DatabaseInitializer.cs
  Security/    JwtOptions.cs, JwtTokenService.cs, AspNetPasswordHasher.cs, SeedOptions.cs
  DependencyInjection.cs
src/OrderManagement.Api/
  Program.cs, appsettings.json, appsettings.Development.json
  Controllers/ AuthController.cs, CustomersController.cs, ProductsController.cs, OrdersController.cs
  Infrastructure/ GlobalExceptionHandler.cs, ValidationFilter.cs, HttpCurrentUser.cs, AuthPolicies.cs, SwaggerSetup.cs
tests/OrderManagement.UnitTests/      Domain/*, Application/*, Infrastructure/JwtTokenServiceTests.cs, Fakes/*
tests/OrderManagement.IntegrationTests/  ApiFactory.cs, ApiCollection.cs, IntegrationTestBase.cs, TestClient.cs, Persistence/*, Api/*
Dockerfile  docker-compose.yml  .env.example  README.md  CLAUDE.md
```

---

### Task 1: Solution scaffold

**Files:**
- Create: `OrderManagement.slnx`, `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig`, `.gitignore` (the standard `dotnet new gitignore` output, plus `.env`), `.config/dotnet-tools.json`
- Create: the 4 `src` projects and 2 `tests` projects listed above, with their project references (§2, plus the UnitTests → Infrastructure reference)
- Test: `tests/OrderManagement.UnitTests/ScaffoldTests.cs`

**Interfaces:**
- Produces: a solution that builds, with central package management (`ManagePackageVersionsCentrally=true`)

- [ ] **Step 1:** Create the solution and projects with `dotnet new sln -n OrderManagement` (this produces `.slnx`), `classlib` for Domain, Application and Infrastructure, `webapi --use-controllers` for Api, and `classlib` for both test projects. Convert the test projects to xunit v3: `OutputType=Exe`, `IsPackable=false`, and references to `xunit.v3`, `xunit.runner.visualstudio` and `Microsoft.NET.Test.Sdk`. Delete the template files (`Class1.cs`, `WeatherForecast*`).
- [ ] **Step 2:** Add `Directory.Build.props` and `Directory.Packages.props` using the Global Constraints values. Add `public partial class Program;` at the end of `Program.cs`. Run `dotnet new tool-manifest` and `dotnet tool install dotnet-ef --version 10.0.12`.
- [ ] **Step 3:** Add the smoke test `ScaffoldTests.Solution_builds_and_tests_run` with `Assert.True(true)`.
- [ ] **Step 4:** Run `dotnet build` (expect 0 warnings, 0 errors) and `dotnet test` (expect 1 passed).
- [ ] **Step 5:** Commit: `Scaffold solution and projects`

---

### Task 2: Domain: exceptions, users, customers, products

**Files:**
- Create: `src/OrderManagement.Domain/Common/*.cs`, `Users/User.cs`, `Users/UserRole.cs`, `Customers/Customer.cs`, `Products/Product.cs`
- Test: `tests/OrderManagement.UnitTests/Domain/ProductTests.cs`, `UserTests.cs`

**Interfaces:**
- Produces:
  - `class DomainException(string message) : Exception`. `NotFoundException(string entity, Guid id)` and `ConflictException(string message)` both derive from it.
  - `enum UserRole { Admin, Customer }`
  - `User`: properties `Id`, `Email`, `PasswordHash`, `Role`, `CreatedAt`. `static User Create(string email, UserRole role, DateTime utcNow)` trims and lower-cases the email. `void SetPasswordHash(string hash)`.
  - `Customer`: properties `Id`, `UserId`, `User User`, `FullName`, `Phone`, `ShippingAddress`. `static Customer Create(User user, string fullName, string phone, string shippingAddress)`. `void UpdateProfile(string fullName, string phone, string shippingAddress)`.
  - `Product`: properties `Id`, `Sku`, `Name`, `string? Description`, `Price`, `StockQuantity`, `IsActive`, `CreatedAt`, `UpdatedAt`. `static Product Create(string sku, string name, string? description, decimal price, int stock, DateTime utcNow)`, `void Update(string name, string? description, decimal price, int stock, DateTime utcNow)`, `void Deactivate(DateTime utcNow)`, `void DeductStock(int qty, DateTime utcNow)`, `void RestoreStock(int qty, DateTime utcNow)`.
  - All entities use private setters and a private parameterless constructor for EF Core.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void DeductStock_reduces_stock()            // stock 5, deduct 3 → StockQuantity.ShouldBe(2)
[Fact] public void DeductStock_below_zero_throws_conflict() // stock 2, deduct 3 → Should.Throw<ConflictException>, stock still 2
[Fact] public void RestoreStock_adds_back()                // stock 2, restore 3 → 5
[Theory, InlineData(0), InlineData(-1)] public void DeductStock_non_positive_qty_throws(int q) // DomainException
[Fact] public void Create_with_non_positive_price_throws()  // price 0 → DomainException
[Fact] public void Create_with_negative_stock_throws()      // DomainException
[Fact] public void Deactivate_sets_IsActive_false_and_UpdatedAt()
[Fact] public void User_Create_normalizes_email()           // " Bob@Example.com " → "bob@example.com"
```

- [ ] **Step 2:** Run `dotnet test tests/OrderManagement.UnitTests --filter "FullyQualifiedName~Domain"` and expect it to FAIL to compile (the types don't exist yet).
- [ ] **Step 3:** Implement the types listed under Interfaces. `Sku` is stored trimmed and upper-cased.
- [ ] **Step 4:** Run the same command and expect all to PASS.
- [ ] **Step 5:** Commit: `Add domain exceptions, users, customers and products`

---

### Task 3: Domain: orders and the status workflow

**Files:**
- Create: `src/OrderManagement.Domain/Orders/Order.cs`, `OrderItem.cs`, `OrderStatus.cs`, `OrderLine.cs`, `OrderNumber.cs`
- Test: `tests/OrderManagement.UnitTests/Domain/OrderTests.cs`

**Interfaces:**
- Consumes: `Product` and the exceptions (Task 2)
- Produces:
  - `enum OrderStatus { Pending, Confirmed, Shipped, Delivered, Completed, Cancelled }`
  - `record OrderLine(Product Product, int Quantity)`
  - `OrderItem`: `Id`, `OrderId`, `ProductId`, `ProductName`, `UnitPrice`, `Quantity`, `LineTotal`
  - `Order`: `Id`, `OrderNumber`, `CustomerId`, `Status`, `TotalAmount`, `CreatedAt`, `UpdatedAt`, `IReadOnlyList<OrderItem> Items`. `static Order Create(Guid customerId, IReadOnlyList<OrderLine> lines, DateTime utcNow)`. `Confirm`, `Ship`, `Deliver`, `Complete` and `Cancel` each take `(DateTime utcNow)`.
  - `static string OrderNumber.Generate(DateTime utcNow)` returns `ORD-yyyyMMdd-XXXXXX`, with 6 characters from `A–Z0–9` chosen by `RandomNumberGenerator`.
  - `Order.Create` does **not** touch stock. The service calls `DeductStock`, so all stock changes stay in one place.

- [ ] **Step 1: Write the failing tests** (the transition table is in spec §3)

```csharp
[Fact] public void Create_copies_name_and_price_and_computes_total() // 2×10.50 + 1×3.00 → TotalAmount 24.00; item.ProductName/UnitPrice copied
[Fact] public void Create_with_no_lines_throws()                     // DomainException
[Fact] public void Create_starts_Pending_with_valid_number()          // Regex ^ORD-\d{8}-[A-Z0-9]{6}$
[Fact] public void Copied_price_unaffected_by_later_product_update()
[Theory, MemberData(nameof(AllowedTransitions))]  public void Allowed_transition_sets_status_and_UpdatedAt(...)
[Theory, MemberData(nameof(RejectedTransitions))] public void Rejected_transition_throws_conflict(...)
// Allowed: Pending→Confirm, Confirmed→Ship, Shipped→Deliver, Delivered→Complete, Pending→Cancel, Confirmed→Cancel
// Rejected: every other (status, action) pair, including any action from Completed or Cancelled
```

- [ ] **Step 2:** Run `--filter "FullyQualifiedName~OrderTests"` and expect FAIL.
- [ ] **Step 3:** Implement. Each transition checks a single "allowed from" set and otherwise throws `ConflictException($"Cannot {action} an order in status {Status}.")`.
- [ ] **Step 4:** Run it again and expect PASS.
- [ ] **Step 5:** Commit: `Add orders and order status workflow`

---

### Task 4: Application foundations and token/password services

**Files:**
- Create: `src/OrderManagement.Application/Common/*.cs`, `Auth/ITokenService.cs`, `Auth/IPasswordHasher.cs`, `DependencyInjection.cs`
- Create: `src/OrderManagement.Domain/Users/RefreshToken.cs`
- Create: `src/OrderManagement.Infrastructure/Security/JwtOptions.cs`, `JwtTokenService.cs`, `AspNetPasswordHasher.cs`
- Test: `tests/OrderManagement.UnitTests/Infrastructure/JwtTokenServiceTests.cs`, `Domain/RefreshTokenTests.cs`

**Interfaces:**
- Produces:
  - `record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)` and `PagedResult<TOut> Map<TOut>(Func<T,TOut>)`
  - `record PageQuery { int Page = 1; int PageSize = 20; }` (a base record for the list queries)
  - `interface ICurrentUser { Guid? UserId; UserRole? Role; bool IsAdmin; }`
  - `interface IUnitOfWork { Task SaveChangesAsync(CancellationToken ct); }`. Implementations translate concurrency and duplicate-key errors into `ConflictException`.
  - `class AuthenticationFailedException(string message) : Exception`. It doesn't derive from `DomainException` and maps to 401.
  - `record AccessToken(string Token, DateTime ExpiresAt)` and `record IssuedRefreshToken(string Token, string Hash, DateTime ExpiresAt)`
  - `interface ITokenService { AccessToken CreateAccessToken(User user); IssuedRefreshToken CreateRefreshToken(); string Hash(string refreshToken); }`
  - `interface IPasswordHasher { string Hash(User user, string password); bool Verify(User user, string password); }`
  - `RefreshToken`: `Id`, `UserId`, `TokenHash`, `ExpiresAt`, `CreatedAt`, `RevokedAt`, `ReplacedByTokenHash`. `static Create(Guid userId, string hash, DateTime expiresAt, DateTime utcNow)`, `bool IsActive(DateTime utcNow)`, `void Revoke(DateTime utcNow, string? replacedByHash = null)`. Revoking an already-revoked token changes nothing.
  - `JwtOptions` (section `"Jwt"`): `Issuer`, `Audience`, `SigningKey`, `AccessTokenMinutes = 15`, `RefreshTokenDays = 7`. A static `Validate` requires all strings to be non-empty and `Encoding.UTF8.GetByteCount(SigningKey) >= 32`.
  - `JwtTokenService(IOptions<JwtOptions>, TimeProvider) : ITokenService`. It uses `JsonWebTokenHandler` with claims `sub`, `email`, `role` and `jti`, and signs with HS256. Refresh tokens are 64 bytes from `RandomNumberGenerator`, Base64Url-encoded. `Hash` is SHA-256 as lowercase hex.
  - `AspNetPasswordHasher : IPasswordHasher` wraps `Microsoft.AspNetCore.Identity.PasswordHasher<User>`. `Verify` returns true for `Success` and `SuccessRehashNeeded`.
  - `services.AddApplication()` registers the validators from the assembly and the services (added in later tasks), and adds `TimeProvider.System` with `TryAddSingleton`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void Access_token_contains_sub_email_role_jti_and_expiry() // FakeTimeProvider at 2026-01-01T00:00Z → ExpiresAt == +15 min; read with JsonWebTokenHandler
[Fact] public void Refresh_tokens_are_unique_and_hash_is_deterministic() // two CreateRefreshToken() differ; Hash(t) == Hash(t) == issued.Hash; Hash length 64
[Fact] public void Refresh_token_expires_after_configured_days()        // ExpiresAt == now + 7 days
[Fact] public void JwtOptions_rejects_short_key()                       // 31-byte key → Validate returns false
[Fact] public void RefreshToken_IsActive_false_when_revoked_or_expired()
```

- [ ] **Step 2:** Run `--filter "FullyQualifiedName~JwtTokenService|FullyQualifiedName~RefreshToken"` and expect FAIL.
- [ ] **Step 3:** Implement the types listed under Interfaces.
- [ ] **Step 4:** Run it again and expect PASS.
- [ ] **Step 5:** Commit: `Add application foundations and JWT token service`

---

### Task 5: Products: service and validators

**Files:**
- Create: `src/OrderManagement.Application/Products/*.cs`
- Create: `tests/OrderManagement.UnitTests/Fakes/FakeCurrentUser.cs`, `FakeUnitOfWork.cs`, `InMemoryProductRepository.cs`
- Test: `tests/OrderManagement.UnitTests/Application/ProductServiceTests.cs`, `ProductValidatorTests.cs`

**Interfaces:**
- Consumes: `Product`, `PagedResult`, `PageQuery`, `ICurrentUser`, `IUnitOfWork` (Tasks 2 and 4)
- Produces:
  - `IProductRepository`: `Task<Product?> GetByIdAsync(Guid id, CancellationToken ct)`, `Task<IReadOnlyList<Product>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)`, `Task<bool> SkuExistsAsync(string sku, Guid? excludeId, CancellationToken ct)`, `void Add(Product product)`, `Task<PagedResult<Product>> ListAsync(ProductListQuery q, bool includeInactive, CancellationToken ct)`. The list is sorted by name, and `Search` matches Name or Sku with a contains check.
  - `record ProductListQuery : PageQuery { string? Search; bool IncludeInactive; }`
  - `record CreateProductRequest(string Sku, string Name, string? Description, decimal Price, int StockQuantity)` and `record UpdateProductRequest(string Name, string? Description, decimal Price, int StockQuantity)`
  - `record ProductResponse(Guid Id, string Sku, string Name, string? Description, decimal Price, int StockQuantity, bool IsActive)`
  - `IProductService`: `GetAsync(Guid id)`, `ListAsync(ProductListQuery q)`, `CreateAsync(CreateProductRequest r)`, `UpdateAsync(Guid id, UpdateProductRequest r)`, `DeactivateAsync(Guid id)`. Each takes a `CancellationToken`.
  - Fakes are reused by Tasks 6–8. `FakeUnitOfWork` counts calls to `SaveChangesAsync`.

- [ ] **Step 1: Write the failing tests**

```csharp
// Service
[Fact] public async Task Non_admin_cannot_see_inactive_product()          // GetAsync → NotFoundException
[Fact] public async Task Admin_sees_inactive_product()
[Fact] public async Task List_ignores_IncludeInactive_for_non_admin()
[Fact] public async Task Create_duplicate_sku_throws_conflict()           // "abc-1" exists, create "ABC-1" → ConflictException
[Fact] public async Task Update_missing_product_throws_not_found()
[Fact] public async Task Deactivate_sets_inactive_and_saves()             // SaveCount 1
// Validators
[Theory, InlineData(0), InlineData(-1)] public void Price_must_be_positive(decimal p)
[Fact] public void Price_with_more_than_two_decimals_is_rejected()        // 19.999 → error on Price   (Review Focus 4)
[Fact] public void Sku_required_max_32_letters_digits_dash()
[Fact] public void Name_required_max_200()
[Fact] public void Stock_must_be_zero_or_more()
[Theory, InlineData(0, 20), InlineData(1, 0), InlineData(1, 101)] public void Page_bounds_rejected(int page, int size) // PageQueryValidator: Page ≥ 1, 1 ≤ PageSize ≤ 100
```

- [ ] **Step 2:** Run `--filter "FullyQualifiedName~Product"` and expect FAIL.
- [ ] **Step 3:** Implement. Use a single `PageQueryValidator<T> : AbstractValidator<T> where T : PageQuery`, which `ProductListQueryValidator` inherits. Check the decimal places with `.PrecisionScale(18, 2, ignoreTrailingZeros: true)`. Map with `ProductMappings.ToResponse(this Product)`. Register in `AddApplication()`.
- [ ] **Step 4:** Run it again and expect PASS.
- [ ] **Step 5:** Commit: `Add product service and validators`

---

### Task 6: Auth: service and validators

**Files:**
- Create: `src/OrderManagement.Application/Auth/IAuthService.cs`, `AuthService.cs`, `IUserRepository.cs`, `IRefreshTokenRepository.cs`, `AuthDtos.cs`, `AuthValidators.cs`, `Customers/ICustomerRepository.cs`
- Create: `tests/OrderManagement.UnitTests/Fakes/InMemoryUserRepository.cs`, `InMemoryRefreshTokenRepository.cs`, `InMemoryCustomerRepository.cs`, `FakeTokenService.cs` (predictable tokens such as `rt-1`, `rt-2`, with hash `h(<token>)`), `FakePasswordHasher.cs` (hash = `"hashed:" + password`)
- Test: `tests/OrderManagement.UnitTests/Application/AuthServiceTests.cs`, `AuthValidatorTests.cs`

**Interfaces:**
- Consumes: `ITokenService`, `IPasswordHasher`, `ICurrentUser`, `IUnitOfWork`, `User`, `Customer`, `RefreshToken`
- Produces:
  - `IUserRepository`: `Task<User?> GetByIdAsync(Guid id, CancellationToken ct)`, `Task<User?> GetByEmailAsync(string normalizedEmail, CancellationToken ct)`, `Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken ct)`, `void Add(User user)`
  - `IRefreshTokenRepository`: `Task<RefreshToken?> GetByHashAsync(string hash, CancellationToken ct)`, `Task<IReadOnlyList<RefreshToken>> GetActiveByUserIdAsync(Guid userId, DateTime utcNow, CancellationToken ct)`, `void Add(RefreshToken token)`
  - `ICustomerRepository`: `Task<Customer?> GetByIdAsync(Guid id, CancellationToken ct)` and `Task<Customer?> GetByUserIdAsync(Guid userId, CancellationToken ct)` (both include `User`), and `void Add(Customer c)`. Task 7 adds `ListAsync`.
  - Requests: `RegisterRequest(string Email, string Password, string FullName, string Phone, string ShippingAddress)`, `LoginRequest(string Email, string Password)`, `RefreshRequest(string RefreshToken)`, `LogoutRequest(string RefreshToken)`
  - `record AuthResponse(string AccessToken, DateTime AccessTokenExpiresAt, string RefreshToken, DateTime RefreshTokenExpiresAt)`
  - `IAuthService`: `RegisterAsync`, `LoginAsync` and `RefreshAsync` return `Task<AuthResponse>`. `LogoutAsync(LogoutRequest, CancellationToken)` returns `Task`.
  - Behavior from spec §5. The user, customer and refresh token are saved with a single `SaveChangesAsync`. Every failure returns `AuthenticationFailedException("Invalid credentials.")` or `("Invalid refresh token.")`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public async Task Register_creates_customer_user_profile_and_tokens()   // Role Customer; Customer row linked; SaveCount 1
[Fact] public async Task Register_duplicate_email_case_insensitive_conflict() // " Bob@Example.com " then "BOB@example.com" → ConflictException (Review Focus 2)
[Fact] public async Task Login_with_different_case_email_succeeds()          // (Review Focus 2)
[Fact] public async Task Login_unknown_email_and_wrong_password_same_error()  // both AuthenticationFailedException, identical Message
[Fact] public async Task Refresh_rotates_token()                             // old revoked, ReplacedByTokenHash == new hash, new pair returned
[Fact] public async Task Refresh_with_revoked_token_revokes_all_and_fails()   // rotate rt-1→rt-2, present rt-1 → throws; rt-2 now revoked
[Fact] public async Task Refresh_with_expired_or_unknown_token_fails()        // FakeTimeProvider advanced 8 days
[Fact] public async Task Logout_revokes_own_token_and_ignores_others()        // other user's token stays active; no exception
// Validators
[Theory, InlineData("short1"), InlineData("lettersonly"), InlineData("12345678")] public void Weak_password_rejected(string p)
[Fact] public void Email_must_be_valid()
[Fact] public void FullName_Phone_Address_required_with_max_lengths() // 200 / 30 / 500
```

- [ ] **Step 2:** Run `--filter "FullyQualifiedName~Auth"` and expect FAIL.
- [ ] **Step 3:** Implement `AuthService(IUserRepository, ICustomerRepository, IRefreshTokenRepository, ITokenService, IPasswordHasher, ICurrentUser, IUnitOfWork, TimeProvider)` and register it. The password rule is `MinimumLength(8)` with the patterns `[A-Za-z]` and `\d`.
- [ ] **Step 4:** Run it again and expect PASS.
- [ ] **Step 5:** Commit: `Add auth service with refresh token rotation`

---

### Task 7: Customers: service and validators

**Files:**
- Create: `src/OrderManagement.Application/Customers/ICustomerService.cs`, `CustomerService.cs`, `CustomerDtos.cs`, `CustomerValidators.cs`, `CustomerMappings.cs`
- Test: `tests/OrderManagement.UnitTests/Application/CustomerServiceTests.cs`

**Interfaces:**
- Consumes: `ICustomerRepository` (Task 6), `ICurrentUser`, `IUnitOfWork`
- Produces:
  - Adds `Task<PagedResult<Customer>> ListAsync(CustomerListQuery q, CancellationToken ct)` to `ICustomerRepository` and to `InMemoryCustomerRepository`.
  - `record CustomerListQuery : PageQuery { string? Search; }`. Search matches FullName or the user's Email. Sorted newest first by the user's CreatedAt.
  - `record UpdateCustomerRequest(string FullName, string Phone, string ShippingAddress)`
  - `record CustomerResponse(Guid Id, string Email, string FullName, string Phone, string ShippingAddress, DateTime CreatedAt)`
  - `ICustomerService`: `GetMeAsync()`, `UpdateMeAsync(UpdateCustomerRequest)`, `GetAsync(Guid id)`, `ListAsync(CustomerListQuery)`. Each takes a `CancellationToken`.
  - Fetching "me" when there's no customer profile for the user throws `NotFoundException`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public async Task GetMe_returns_current_users_profile_with_email()
[Fact] public async Task UpdateMe_changes_profile_and_saves()
[Fact] public async Task Get_unknown_id_throws_not_found()
[Fact] public async Task List_filters_by_search_and_pages() // 3 customers, Search "ann", PageSize 1 → TotalCount 2, Items.Count 1
```

- [ ] **Step 2:** Run `--filter "FullyQualifiedName~CustomerService"` and expect FAIL.
- [ ] **Step 3:** Implement and register. `UpdateCustomerRequestValidator` uses the same length limits as registration.
- [ ] **Step 4:** Run it again and expect PASS.
- [ ] **Step 5:** Commit: `Add customer service`

---

### Task 8: Orders: service and validators

**Files:**
- Create: `src/OrderManagement.Application/Orders/*.cs`
- Create: `tests/OrderManagement.UnitTests/Fakes/InMemoryOrderRepository.cs`
- Test: `tests/OrderManagement.UnitTests/Application/OrderServiceTests.cs`, `OrderValidatorTests.cs`

**Interfaces:**
- Consumes: `Order`, `OrderLine`, `IProductRepository`, `ICustomerRepository`, `ICurrentUser`, `IUnitOfWork`, `TimeProvider`
- Produces:
  - `IOrderRepository`: `Task<Order?> GetByIdAsync(Guid id, CancellationToken ct)` (includes Items), `void Add(Order o)`, `Task<PagedResult<Order>> ListAsync(OrderListQuery q, Guid? customerId, CancellationToken ct)` (newest first)
  - `record OrderListQuery : PageQuery { OrderStatus? Status; Guid? CustomerId; DateTime? From; DateTime? To; }`. For `/orders/my` the service ignores the `CustomerId` field and uses the caller's own ID.
  - `record PlaceOrderRequest(IReadOnlyList<PlaceOrderItem> Items)` and `record PlaceOrderItem(Guid ProductId, int Quantity)`
  - `record OrderResponse(Guid Id, string OrderNumber, Guid CustomerId, OrderStatus Status, decimal TotalAmount, DateTime CreatedAt, DateTime UpdatedAt, IReadOnlyList<OrderItemResponse> Items)` and `record OrderItemResponse(Guid ProductId, string ProductName, decimal UnitPrice, int Quantity, decimal LineTotal)`
  - `IOrderService`: `PlaceAsync(PlaceOrderRequest)`, `GetAsync(Guid id)`, `ListMineAsync(OrderListQuery)`, `ListAsync(OrderListQuery)`, and `ConfirmAsync`, `ShipAsync`, `DeliverAsync`, `CompleteAsync`, `CancelAsync`, each taking `(Guid id)`. Every one returns `OrderResponse` (lists return `PagedResult<OrderResponse>`) and takes a `CancellationToken`.
  - Access rules: Confirm, Ship and Deliver are restricted to admins by the controller policy. Get, Complete and Cancel load the order through a private `LoadAccessibleAsync(id)`. For a non-admin it throws `NotFoundException` unless `order.CustomerId` belongs to the caller's customer profile.

- [ ] **Step 1: Write the failing tests** (the flow is in spec §4, "Placing an order")

```csharp
[Fact] public async Task Place_deducts_stock_copies_prices_and_saves_once()
[Fact] public async Task Place_missing_or_inactive_product_throws_not_found() // and no stock changed on other products
[Fact] public async Task Place_insufficient_stock_throws_conflict()
[Fact] public async Task Get_other_customers_order_throws_not_found()
[Fact] public async Task Admin_can_get_any_order()
[Fact] public async Task Cancel_restores_stock_for_every_item()
[Fact] public async Task Cancel_restores_stock_even_if_product_deactivated()  // (Review Focus 3) GetByIdsAsync must return inactive products too
[Fact] public async Task Customer_can_complete_own_delivered_order()
[Fact] public async Task Complete_before_delivered_throws_conflict()
[Fact] public async Task ListMine_returns_only_callers_orders()
// Validators
[Fact] public void Empty_items_rejected()
[Fact] public void Duplicate_product_ids_rejected()
[Theory, InlineData(0), InlineData(1001)] public void Quantity_out_of_range_rejected(int q)
[Fact] public void From_after_To_rejected()
```

- [ ] **Step 2:** Run `--filter "FullyQualifiedName~Order"` and expect FAIL.
- [ ] **Step 3:** Implement and register. `GetByIdsAsync` returns products whatever their `IsActive` value, and `PlaceAsync` rejects inactive ones itself.
- [ ] **Step 4:** Run `dotnet test tests/OrderManagement.UnitTests` and expect all to PASS.
- [ ] **Step 5:** Commit: `Add order service and validators`

---

### Task 9: Persistence: DbContext, repositories, migrations and seeding

**Files:**
- Create: `src/OrderManagement.Infrastructure/Persistence/**`, `Security/SeedOptions.cs`, `DependencyInjection.cs`
- Create: `tests/OrderManagement.IntegrationTests/MySqlFixture.cs`, `Persistence/PersistenceTests.cs`

**Interfaces:**
- Consumes: every repository interface, `IUnitOfWork`, `JwtOptions`, `JwtTokenService`, `AspNetPasswordHasher`
- Produces:
  - `AppDbContext : DbContext, IUnitOfWork`, with `DbSet`s for Users, Customers, Products, Orders, OrderItems and RefreshTokens. It applies the configurations from its own assembly.
  - `SaveChangesAsync` (the `IUnitOfWork` version) catches `DbUpdateConcurrencyException`, rethrowing it as `ConflictException("The data was changed by another request. Please retry.")`. It also catches `DbUpdateException` whose inner exception is a `MySql.Data.MySqlClient.MySqlException` with `Number == 1062`, rethrowing as `ConflictException("A record with the same unique value already exists.")`.
  - Configurations:
    - Unique indexes on `Users.Email`, `Customers.UserId`, `Products.Sku`, `Orders.OrderNumber` and `RefreshTokens.TokenHash`.
    - `Product.StockQuantity` is a concurrency token (`.IsConcurrencyToken()`).
    - Money is `decimal(18,2)` and enums are stored as strings (maximum 20 characters).
    - Maximum lengths: Email 256, PasswordHash 512, FullName 200, Phone 30, ShippingAddress 500, Sku 32, Name 200, Description 2000, OrderNumber 20, TokenHash 64.
    - `Order.Items` is accessed through a backing field, with cascade delete. Orders → Customer and OrderItem → Product use `Restrict`.
    - Indexes on `Orders(CustomerId, CreatedAt)` and `Orders(Status)`.
  - Repositories in `Persistence/Repositories/`, one class per interface.
  - `SeedOptions` (section `"Seed"`): `AdminEmail` and `AdminPassword`, both required.
  - `DatabaseInitializer(AppDbContext, IPasswordHasher, IOptions<SeedOptions>, TimeProvider)` with `MigrateAsync(ct)` and `SeedAsync(ct)`. Seeding only adds the admin if the email doesn't exist, and adds 5 sample products (SKUs `SAMPLE-001`…`SAMPLE-005`) if no products exist.
  - `services.AddInfrastructure(IConfiguration)` registers `UseMySQL(ConnectionStrings:Default)`, the repositories, `IUnitOfWork` (resolved from `AppDbContext`), `ITokenService`, `IPasswordHasher`, `DatabaseInitializer`, and `JwtOptions` / `SeedOptions` with `.ValidateOnStart()`.
  - `AppDbContextDesignTimeFactory : IDesignTimeDbContextFactory<AppDbContext>` uses a placeholder connection string, so generating migrations never needs a database or the Api host.
  - The first migration is named `InitialCreate`: `dotnet tool run dotnet-ef migrations add InitialCreate -p src/OrderManagement.Infrastructure -s src/OrderManagement.Infrastructure -o Persistence/Migrations`. Infrastructure references `Microsoft.EntityFrameworkCore.Design` with `PrivateAssets=all`.
  - `MySqlFixture : IAsyncLifetime` starts a `MySqlBuilder().WithImage("mysql:8.4")` container, runs the migrations once, and exposes `CreateDbContext()` and `ResetAsync()`. `ResetAsync` truncates every table with `FOREIGN_KEY_CHECKS=0`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public async Task Duplicate_email_save_throws_conflict()             // via SaveChangesAsync → ConflictException
[Fact] public async Task Stale_stock_update_throws_conflict()               // two contexts load same product, both DeductStock, second save → ConflictException
[Fact] public async Task Order_with_items_round_trips()                     // money and enum-as-string preserved
[Fact] public async Task Product_list_search_and_paging_work()              // Search hits Name and Sku; TotalCount correct
[Fact] public async Task Seed_is_idempotent()                               // SeedAsync twice → 1 admin, 5 products
```

- [ ] **Step 2:** Run `dotnet test tests/OrderManagement.IntegrationTests --filter "FullyQualifiedName~Persistence"` and expect FAIL.
- [ ] **Step 3:** Implement, then create the `InitialCreate` migration. Look over the generated migration: there should be 6 tables, the unique indexes, `char(36)` GUIDs and `decimal(18,2)`.
- [ ] **Step 4:** Run it again and expect PASS. Docker must be running.
- [ ] **Step 5:** Commit: `Add EF Core persistence, migrations and seeding`

---

### Task 10: API host and auth endpoints

**Files:**
- Modify: `src/OrderManagement.Api/Program.cs`, `appsettings.json`, `appsettings.Development.json`
- Create: `src/OrderManagement.Api/Infrastructure/*.cs`, `Controllers/AuthController.cs`
- Create: `tests/OrderManagement.IntegrationTests/ApiFactory.cs`, `ApiCollection.cs`, `IntegrationTestBase.cs`, `TestClient.cs`
- Test: `tests/OrderManagement.IntegrationTests/Api/AuthEndpointsTests.cs`, `ErrorHandlingTests.cs`

**Interfaces:**
- Consumes: `AddApplication()`, `AddInfrastructure()`, `IAuthService`, `DatabaseInitializer`, `AuthenticationFailedException`, the domain exceptions
- Produces:
  - In `Program.cs`, in order:
    - Serilog with `UseSerilogRequestLogging`. Never log request bodies or the `Authorization` header.
    - Controllers with `JsonStringEnumConverter` and the global `ValidationFilter`.
    - `AddProblemDetails` with a customization that adds `traceId`, plus `AddExceptionHandler<GlobalExceptionHandler>`.
    - JWT bearer validation using `JwtOptions`, with `ClockSkew = 30s`, `MapInboundClaims = false`, `NameClaimType = "sub"` and `RoleClaimType = "role"`.
    - Authorization policies `AuthPolicies.AdminOnly` and `AuthPolicies.CustomerOnly`.
    - `HttpCurrentUser : ICurrentUser` as a scoped service.
    - Swagger with a Bearer security scheme (Swashbuckle 10 uses Microsoft.OpenApi v2: `OpenApiSecuritySchemeReference`), enabled in the `Development` and `Docker` environments.
    - `AddHealthChecks().AddDbContextCheck<AppDbContext>()` mapped at `/health`.
    - In `Development` and `Docker`, run `DatabaseInitializer.MigrateAsync` and then `SeedAsync` on startup.
  - `ValidationFilter`: an `IAsyncActionFilter` that, for each action argument, resolves `IValidator<T>` if one is registered. On failure it returns `ValidationProblem` (400) with `errors` per field.
  - `GlobalExceptionHandler : IExceptionHandler` maps exceptions to status codes as in spec §6. A `BadHttpRequestException` or a JSON body that can't be read is already 400 through `[ApiController]`. Confirm that with the test below.
  - `AuthController` at `api/v1/auth`: `register` returns 201, `login` and `refresh` return 200, and `logout` (`[Authorize]`) returns 204.
  - `ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime` owns the MySQL container. It sets the environment to `Testing` and overrides `ConnectionStrings:Default`, `Jwt:*` (a fixed 48-character test key) and `Seed:*`. It runs the migrations once.
  - `IntegrationTestBase : IAsyncLifetime` belongs to collection `"Api"`. Before each test it calls reset and then seed.
  - `TestClient` helpers: `RegisterCustomerAsync(email?)`, `LoginAdminAsync()` returning an authenticated `HttpClient`, and `CreateProductAsync(stock, price)`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public async Task Register_returns_201_with_tokens()
[Fact] public async Task Register_duplicate_email_returns_409_problem()
[Fact] public async Task Login_bad_credentials_returns_401_problem()
[Fact] public async Task Refresh_rotates_and_old_token_reuse_returns_401_and_revokes_new() // spec §5 reuse rule end-to-end
[Fact] public async Task Logout_returns_204_and_token_no_longer_refreshes()
[Fact] public async Task Expired_access_token_returns_401()     // token minted with test key, exp in past (beyond 30s skew)
[Fact] public async Task Tampered_access_token_returns_401()    // flip last signature char
[Fact] public async Task Validation_error_returns_400_with_field_errors() // weak password → errors["Password"]
[Fact] public async Task Malformed_json_returns_400_problem()  // body "{\"email\":" (Review Focus 1)
[Fact] public async Task Problem_responses_include_traceId()
[Fact] public async Task Health_returns_200()
```

- [ ] **Step 2:** Run `--filter "FullyQualifiedName~AuthEndpoints|FullyQualifiedName~ErrorHandling"` and expect FAIL.
- [ ] **Step 3:** Implement the pieces listed under Interfaces.
- [ ] **Step 4:** Run it again and expect PASS. Then run `dotnet run --project src/OrderManagement.Api` against a local MySQL (or wait for Task 13 and use compose) and check that `/swagger` loads.
- [ ] **Step 5:** Commit: `Add API host, error handling and auth endpoints`

---

### Task 11: Product and customer endpoints

**Files:**
- Create: `src/OrderManagement.Api/Controllers/ProductsController.cs`, `CustomersController.cs`
- Test: `tests/OrderManagement.IntegrationTests/Api/ProductEndpointsTests.cs`, `CustomerEndpointsTests.cs`

**Interfaces:**
- Consumes: `IProductService`, `ICustomerService`, `AuthPolicies`
- Produces:
  - Routes as in spec §4, all using `{id:guid}`. `POST /products` returns 201 with a `Location` header pointing to `GET /products/{id}`. `DELETE` returns 204.
  - `GET /customers/me` is declared before `GET /customers/{id:guid}`. The `:guid` constraint means `me` never matches the ID route.
  - Query objects are bound with `[FromQuery]`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public async Task Anonymous_can_list_active_products_only()
[Fact] public async Task Admin_crud_product_flow()                       // create 201 → update 200 → delete 204 → anonymous GET 404 → admin GET includeInactive 200
[Fact] public async Task Customer_cannot_create_product_403()
[Fact] public async Task Anonymous_cannot_create_product_401()
[Fact] public async Task PageSize_over_100_returns_400()
[Fact] public async Task Customer_gets_and_updates_own_profile()
[Fact] public async Task Customer_cannot_list_customers_403()
[Fact] public async Task Admin_lists_customers_with_search()
[Fact] public async Task Admin_cannot_use_customers_me_403()
```

- [ ] **Step 2:** Run `--filter "FullyQualifiedName~ProductEndpoints|FullyQualifiedName~CustomerEndpoints"` and expect FAIL.
- [ ] **Step 3:** Implement both controllers.
- [ ] **Step 4:** Run it again and expect PASS.
- [ ] **Step 5:** Commit: `Add product and customer endpoints`

---

### Task 12: Order endpoints

**Files:**
- Create: `src/OrderManagement.Api/Controllers/OrdersController.cs`
- Test: `tests/OrderManagement.IntegrationTests/Api/OrderEndpointsTests.cs`, `OrderConcurrencyTests.cs`

**Interfaces:**
- Consumes: `IOrderService`, `AuthPolicies`
- Produces:
  - Routes as in spec §4. `POST /orders` returns 201 with a `Location` header pointing to `GET /orders/{id}`. Action endpoints return 200 with an `OrderResponse`.
  - `my` is a fixed route segment and IDs use `{id:guid}`, so the two never clash.
  - Policies: `POST /orders` and `GET /orders/my` require CustomerOnly. `GET /orders`, `confirm`, `ship` and `deliver` require AdminOnly. `GET /orders/{id}`, `complete` and `cancel` only require `[Authorize]`, and the service checks ownership.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public async Task Full_lifecycle_to_completed()                   // place → admin confirm/ship/deliver → customer complete; stock deducted once
[Fact] public async Task Cancel_restores_stock()
[Fact] public async Task Illegal_transition_returns_409()                // ship a Pending order
[Fact] public async Task Insufficient_stock_returns_409()
[Fact] public async Task Other_customers_order_returns_404()             // GET and cancel
[Fact] public async Task Customer_cannot_confirm_403()
[Fact] public async Task Admin_cannot_place_order_403()
[Fact] public async Task Admin_filters_orders_by_status()
[Fact] public async Task Non_guid_id_returns_404()                       // GET /api/v1/orders/abc (Review Focus 5)
[Fact] public async Task Unknown_status_filter_returns_400()             // ?status=Foo (Review Focus 5)
// OrderConcurrencyTests
[Fact] public async Task Two_parallel_orders_for_last_unit_exactly_one_succeeds() // stock 1, two customers, Task.WhenAll → one 201, one 409; final stock 0
```

- [ ] **Step 2:** Run `--filter "FullyQualifiedName~Order"` in IntegrationTests and expect FAIL.
- [ ] **Step 3:** Implement the controller. If `?status=Foo` returns 200 with the filter ignored instead of 400, make model-binding errors produce a 400 response. `[ApiController]` does this when `ModelState` is invalid, so check that the property is a nullable enum, not a string.
- [ ] **Step 4:** Run `dotnet test` (the full solution) and expect all to PASS.
- [ ] **Step 5:** Commit: `Add order endpoints`

---

### Task 13: Docker, README and CLAUDE.md

**Files:**
- Create: `Dockerfile`, `.dockerignore`, `docker-compose.yml`, `.env.example`, `src/OrderManagement.Api/appsettings.Docker.json`
- Modify: `README.md`, `CLAUDE.md`

**Interfaces:**
- Consumes: the full API

- [ ] **Step 1:** Write the `Dockerfile` as described in spec §9: built with `mcr.microsoft.com/dotnet/sdk:10.0`, restoring the `.csproj` files first so the layers cache well, then `dotnet publish -c Release`, and running on `mcr.microsoft.com/dotnet/aspnet:10.0` with `USER app` and `ASPNETCORE_HTTP_PORTS=8080`.
- [ ] **Step 2:** Write `docker-compose.yml`:
  - The `mysql` service uses `mysql:8.4` with `--character-set-server=utf8mb4 --collation-server=utf8mb4_0900_ai_ci`, the volume `mysql-data`, and the health check `mysqladmin ping -h localhost`.
  - The `api` service depends on `mysql` with `condition: service_healthy`, sets `ASPNETCORE_ENVIRONMENT=Docker`, maps `8080:8080`, and reads `env_file: .env`.
  - `.env.example` lists `MYSQL_ROOT_PASSWORD`, `MYSQL_DATABASE=ordermanagement`, `MYSQL_USER`, `MYSQL_PASSWORD`, `ConnectionStrings__Default`, `Jwt__SigningKey`, `Seed__AdminEmail` and `Seed__AdminPassword`.
- [ ] **Step 3:** Verify. Run `copy .env.example .env`, fill in the values, then `docker compose up --build -d`. Expect `curl http://localhost:8080/health` to return `Healthy` and `http://localhost:8080/swagger` to load. Log in as the seeded admin through Swagger and list the 5 sample products. Run `docker compose down`.
- [ ] **Step 4:** Rewrite `README.md`: what the project is, the architecture diagram as text, how to run with Docker, how to run locally with `dotnet user-secrets` (with the exact keys), `dotnet test`, running a single test with `dotnet test --filter "FullyQualifiedName~<Name>"`, and the note that integration tests need Docker. Rewrite `CLAUDE.md`: the commands, how the layers depend on each other, where business rules live (domain methods), how errors become ProblemDetails, and how the integration tests are set up (`ApiFactory`, resetting before each test).
- [ ] **Step 5:** Commit: `Add Docker setup and documentation`
