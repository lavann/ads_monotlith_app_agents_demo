# Test Strategy for RetailMonolith

## Purpose

This document defines the testing strategy for the RetailMonolith application before modernization. The goal is to establish a comprehensive test baseline that protects existing behavior during the migration from monolith to microservices.

## Testing Pyramid

We follow the standard testing pyramid approach:

```
         /\
        /  \  E2E/Smoke (Manual + Basic Automated)
       /    \
      /------\  Integration Tests
     /        \
    /----------\  Unit Tests
   /--------------\
```

### Unit Tests (Base Layer)
- **Coverage**: 70%+ of business logic
- **Scope**: Services, business rules, calculations
- **Speed**: Fast (<100ms per test)
- **Isolation**: Fully isolated with mocks/stubs
- **Framework**: xUnit + Moq + FluentAssertions

### Integration Tests (Middle Layer)
- **Coverage**: Critical API endpoints and database operations
- **Scope**: Full request/response cycle with real database
- **Speed**: Medium (100ms-1s per test)
- **Isolation**: In-memory database or test database
- **Framework**: xUnit + WebApplicationFactory + Testcontainers (optional)

### Smoke Tests (Top Layer)
- **Coverage**: Critical user journeys
- **Scope**: End-to-end business flows
- **Speed**: Slow (1s-10s per test)
- **Isolation**: Full application stack
- **Framework**: xUnit + WebApplicationFactory

---

## Test Categories

### 1. Unit Tests

#### 1.1 CartService Tests
**File**: `RetailMonolith.UnitTests/Services/CartServiceTests.cs`

**Critical Scenarios**:
- ✅ `AddToCartAsync_WithNewProduct_AddsNewCartLine` - Verifies new product addition
- ✅ `AddToCartAsync_WithExistingProduct_IncrementsQuantity` - Verifies quantity increment
- ✅ `AddToCartAsync_WithInvalidProductId_ThrowsException` - Validates error handling
- ✅ `GetOrCreateCartAsync_WithExistingCart_ReturnsCart` - Tests cart retrieval
- ✅ `GetOrCreateCartAsync_WithNewCustomer_CreatesCart` - Tests cart creation
- ✅ `ClearCartAsync_WithExistingCart_RemovesCart` - Tests cart deletion
- ✅ `GetCartWithLinesAsync_WithNoCart_ReturnsEmptyCart` - Tests empty cart behavior

**Business Rules Validated**:
- Cart is created automatically for new customers
- Existing products in cart have quantity incremented, not duplicated
- Cart includes all line items when retrieved
- Invalid product IDs throw exceptions

#### 1.2 CheckoutService Tests
**File**: `RetailMonolith.UnitTests/Services/CheckoutServiceTests.cs`

**Critical Scenarios**:
- ✅ `CheckoutAsync_WithValidCart_CreatesOrder` - Happy path
- ✅ `CheckoutAsync_WithEmptyCart_ThrowsException` - Validates cart existence
- ✅ `CheckoutAsync_WithInsufficientInventory_ThrowsException` - Stock validation
- ✅ `CheckoutAsync_WithSuccessfulPayment_SetsOrderStatusToPaid` - Payment success flow
- ✅ `CheckoutAsync_WithFailedPayment_SetsOrderStatusToFailed` - Payment failure flow
- ✅ `CheckoutAsync_DecrementsInventory` - Inventory reservation
- ✅ `CheckoutAsync_ClearsCartLines` - Cart cleanup
- ✅ `CheckoutAsync_CalculatesTotalCorrectly` - Total calculation

**Business Rules Validated**:
- Total calculated correctly: Sum(UnitPrice × Quantity)
- Inventory decremented by ordered quantity
- Payment processed with correct amount and currency (GBP)
- Order status set based on payment result
- Cart lines removed after successful checkout
- Order lines match cart lines
- Out-of-stock scenario handled gracefully

#### 1.3 MockPaymentGateway Tests
**File**: `RetailMonolith.UnitTests/Services/MockPaymentGatewayTests.cs`

**Critical Scenarios**:
- ✅ `ChargeAsync_ReturnsSuccessfulResult` - Verifies mock always succeeds
- ✅ `ChargeAsync_ReturnsProviderReference` - Verifies reference generation
- ✅ `ChargeAsync_WithAnyAmount_Succeeds` - Validates no amount checks

**Business Rules Validated**:
- Mock always returns success (for baseline testing)
- Provider reference generated with "MOCK-" prefix
- No validation on payment token

### 2. Integration Tests

#### 2.1 Checkout Flow Integration Tests
**File**: `RetailMonolith.IntegrationTests/Api/CheckoutApiTests.cs`

**Critical Scenarios**:
- ✅ `PostCheckout_WithValidCart_ReturnsOrderWithPaidStatus` - Full checkout flow
- ✅ `PostCheckout_WithEmptyCart_ReturnsBadRequest` - Error handling
- ✅ `GetOrder_AfterCheckout_ReturnsCorrectOrderDetails` - Order retrieval
- ✅ `PostCheckout_DecrementsInventory_InDatabase` - Database state validation

**Integration Points Tested**:
- `/api/checkout` endpoint (POST)
- `/api/orders/{id}` endpoint (GET)
- Database: Carts, CartLines, Inventory, Orders, OrderLines tables
- Service dependencies: CartService → CheckoutService → PaymentGateway

**Data Flow Validated**:
```
1. Add product to cart (setup)
2. POST /api/checkout
3. Verify: Order created in database
4. Verify: Inventory decremented
5. Verify: Cart cleared
6. GET /api/orders/{id}
7. Verify: Order details match
```

#### 2.2 Health Check Tests
**File**: `RetailMonolith.IntegrationTests/Api/HealthCheckTests.cs`

**Critical Scenarios**:
- ✅ `HealthEndpoint_ReturnsHealthy` - Application health
- ✅ `HealthEndpoint_WithDatabaseConnection_ReturnsHealthy` - Database connectivity

**Integration Points Tested**:
- `/health` endpoint
- Database connection pool

#### 2.3 Cart Operations Integration Tests
**File**: `RetailMonolith.IntegrationTests/Api/CartIntegrationTests.cs`

**Critical Scenarios**:
- ✅ `AddToCart_AndRetrieve_ReturnsCorrectItems` - Cart persistence
- ✅ `AddToCart_Multiple_TimesAggregatesQuantity` - Quantity aggregation
- ✅ `ClearCart_RemovesAllItems` - Cart cleanup

**Integration Points Tested**:
- CartService with real database
- Database: Carts, CartLines, Products tables

### 3. Smoke Tests

#### 3.1 Critical User Journeys
**File**: `RetailMonolith.IntegrationTests/Smoke/CriticalFlowTests.cs`

**Critical Scenarios**:
- ✅ `CompleteCheckoutJourney_FromBrowseToOrderConfirmation` - End-to-end happy path
- ✅ `ViewOrderHistory_ReturnsOrders` - Order retrieval

**User Journey Tested**:
```
1. Browse products (GET /Products)
2. Add product to cart (POST /Products)
3. View cart (GET /Cart)
4. Checkout (POST /Checkout)
5. View order details (GET /Orders/Details?id={id})
6. View order history (GET /Orders)
```

**Business Value Validated**:
- Complete checkout flow works end-to-end
- Users can browse, add to cart, checkout, and view orders
- No broken links or 500 errors in critical path

---

## Critical Flows Covered

### Priority 1: Must Work (Checkout Flow)
1. **Add to Cart** - Users can add products to cart
2. **View Cart** - Users can see cart contents and total
3. **Checkout** - Users can complete purchase
4. **Inventory Management** - Stock levels updated correctly
5. **Order Creation** - Orders persisted with correct details
6. **View Order** - Users can view order confirmation

**Coverage**: Integration tests + Smoke tests

### Priority 2: Should Work (Product Browsing)
1. **Browse Products** - Users can see product catalog
2. **View Order History** - Users can see past orders

**Coverage**: Smoke tests

### Priority 3: Nice to Have (Health & Monitoring)
1. **Health Checks** - Application reports health status

**Coverage**: Integration tests

---

## Known Gaps

### 1. Authentication & Authorization
**Gap**: No tests for user authentication or authorization
**Reason**: Current system uses hardcoded "guest" customer ID
**Impact**: Medium - Not tested, but also not implemented
**Plan**: Add tests when authentication is added (Slice 8 in migration plan)

### 2. Payment Gateway Integration
**Gap**: No tests for real payment gateway integration
**Reason**: Using MockPaymentGateway that always succeeds
**Impact**: Medium - Real payment failures not tested
**Plan**: Add tests when real payment gateway integrated
**Mitigation**: Unit tests verify payment failure handling logic exists

### 3. Concurrency & Race Conditions
**Gap**: No tests for concurrent cart operations or inventory conflicts
**Reason**: Optimistic concurrency not validated
**Impact**: Medium - Could oversell inventory under load
**Plan**: Add load tests in Slice 0 (baseline metrics)
**Mitigation**: Database constraints prevent duplicate cart lines

### 4. UI/Razor Pages
**Gap**: Limited coverage of Razor Pages (PageModels only indirectly tested)
**Reason**: Focus on API and service layer
**Impact**: Low - UI changes won't break API contracts
**Plan**: Consider Playwright/Selenium if UI regressions occur
**Mitigation**: Smoke tests cover critical page loads

### 5. Error Handling & Edge Cases
**Gap**: Limited negative testing (malformed input, SQL injection, etc.)
**Reason**: Baseline testing focused on happy paths and known failure modes
**Impact**: Low - Basic validation exists, detailed edge cases not critical for modernization
**Plan**: Add security tests in separate security review
**Mitigation**: Entity Framework prevents SQL injection

### 6. Performance & Load Testing
**Gap**: No performance benchmarks or load tests in this test suite
**Reason**: Performance testing requires dedicated tools (K6, JMeter)
**Impact**: Medium - Performance regressions won't be caught
**Plan**: Slice 0 establishes baseline metrics with K6
**Mitigation**: Integration tests have reasonable timeouts

### 7. Database Migration Testing
**Gap**: No tests for EF Core migrations or data seeding
**Reason**: Migrations tested manually during development
**Impact**: Low - Database schema stable before modernization
**Plan**: Test migrations in Slice 0 (containerization)
**Mitigation**: Migrations run automatically on startup

### 8. Event Publishing (Future)
**Gap**: No tests for event publishing (commented in CheckoutService)
**Reason**: Events not yet implemented
**Impact**: None - Feature doesn't exist
**Plan**: Add tests when events added (Slice 3 - Checkout Service extraction)

---

## Testing Tools & Frameworks

### Required NuGet Packages

**Unit Testing**:
```xml
<PackageReference Include="xUnit" Version="2.9.2" />
<PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
<PackageReference Include="Moq" Version="4.20.72" />
<PackageReference Include="FluentAssertions" Version="6.12.2" />
<PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
```

**Integration Testing**:
```xml
<PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="8.0.11" />
<PackageReference Include="Microsoft.EntityFrameworkCore.InMemory" Version="9.0.9" />
```

### Test Database Strategy

**For Integration Tests**:
- Use **In-Memory Database** (`UseInMemoryDatabase`) for speed and isolation
- Alternative: **SQLite In-Memory** for closer-to-production behavior
- Future: **Testcontainers SQL Server** for exact production parity (Slice 0+)

**Rationale**:
- In-memory database is fast and doesn't require external dependencies
- Sufficient for testing business logic and data access patterns
- SQL Server Testcontainers can be added later for migration validation

---

## Test Execution Strategy

### Local Development
```bash
# Run all tests
dotnet test

# Run only unit tests
dotnet test --filter Category=Unit

# Run only integration tests
dotnet test --filter Category=Integration

# Run with coverage
dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=opencover
```

### CI/CD Pipeline (GitHub Actions)
```yaml
- Run unit tests (fast feedback, <30s)
- Run integration tests (medium feedback, 1-2 min)
- Run smoke tests (comprehensive, 2-5 min)
- Collect coverage report
- Fail build if any test fails
- Fail build if coverage < 70%
```

### Test Execution Order
1. **Unit Tests** - Run first (fastest feedback)
2. **Integration Tests** - Run second (validate integration points)
3. **Smoke Tests** - Run last (validate critical flows)

---

## Success Criteria

### Test Coverage Targets
- **Unit Test Coverage**: ≥ 70% of service layer code
- **Integration Test Coverage**: 100% of critical API endpoints
- **Smoke Test Coverage**: 100% of critical user journeys

### Quality Gates
- ✅ All tests must pass (0 failures)
- ✅ No flaky tests (tests pass consistently)
- ✅ Test execution time < 5 minutes (total)
- ✅ No test warnings or console errors
- ✅ Tests run successfully in CI/CD

### Code Quality
- ✅ Tests follow AAA pattern (Arrange, Act, Assert)
- ✅ Tests have clear, descriptive names
- ✅ Tests are isolated and independent
- ✅ Tests use proper assertions (FluentAssertions)
- ✅ Tests clean up resources (IDisposable)

---

## Maintenance Plan

### Test Ownership
- **Unit Tests**: Development team maintains alongside feature code
- **Integration Tests**: Development team + QA validates scenarios
- **Smoke Tests**: Product owner defines critical flows, dev team implements

### Test Updates
- **When to Update**: 
  - Before any code change (TDD approach preferred)
  - When bugs are found (regression tests)
  - When new features added (expand coverage)
- **Review Process**: Tests reviewed in PR alongside code changes

### Test Debt
- **Track**: Use GitHub Issues labeled "test-debt"
- **Prioritize**: Address before modernization slice begins
- **Review**: Quarterly review of test gaps and coverage

---

## Risks & Mitigations

| Risk | Impact | Probability | Mitigation |
|------|--------|-------------|------------|
| **Tests don't catch regressions** | High | Medium | Add more integration tests, review gaps quarterly |
| **Tests too slow, slowing development** | Medium | Low | Keep unit tests fast (<100ms), optimize integration tests |
| **Flaky tests in CI/CD** | High | Medium | Fix flakiness immediately, investigate root cause |
| **Mocks diverge from real implementation** | Medium | Medium | Keep mocks simple, use integration tests for real behavior |
| **Test maintenance burden** | Medium | Low | Follow DRY principles, use test helpers and fixtures |

---

## Next Steps

### Immediate (This PR)
1. ✅ Create test projects (UnitTests and IntegrationTests)
2. ✅ Implement unit tests for CartService, CheckoutService, MockPaymentGateway
3. ✅ Implement integration tests for /api/checkout and /api/orders/{id}
4. ✅ Implement smoke tests for critical checkout flow
5. ✅ Add GitHub Actions workflow to run tests on PR
6. ✅ Verify all tests pass

### Short-Term (Before Slice 0)
- Add test coverage reporting (Coverlet + CodeCov)
- Add load testing baseline (K6 scripts)
- Document test data setup patterns
- Create test data factories for common scenarios

### Long-Term (During Modernization)
- Add contract tests between services (Pact or similar)
- Add end-to-end tests with Playwright for UI validation
- Implement chaos testing (service failures, timeouts)
- Add performance regression tests

---

## Appendix: Test Naming Conventions

### Unit Tests
Pattern: `MethodName_Scenario_ExpectedResult`

Examples:
- `AddToCartAsync_WithNewProduct_AddsNewCartLine`
- `CheckoutAsync_WithInsufficientInventory_ThrowsException`
- `ChargeAsync_ReturnsSuccessfulResult`

### Integration Tests
Pattern: `HttpMethod_Endpoint_Scenario_ExpectedResult`

Examples:
- `PostCheckout_WithValidCart_ReturnsOrderWithPaidStatus`
- `GetOrder_AfterCheckout_ReturnsCorrectOrderDetails`
- `HealthEndpoint_ReturnsHealthy`

### Smoke Tests
Pattern: `UserJourney_Scenario`

Examples:
- `CompleteCheckoutJourney_FromBrowseToOrderConfirmation`
- `ViewOrderHistory_ReturnsOrders`

---

## Summary

This test strategy provides a comprehensive baseline to protect existing behavior during modernization. The focus is on:

1. **Core Business Logic**: Unit tests ensure services work correctly in isolation
2. **Critical API Endpoints**: Integration tests validate end-to-end flows with real database
3. **User Journeys**: Smoke tests confirm critical paths work from user perspective

**Coverage Priority**: Checkout flow > Product browsing > Health checks

**Known Gaps**: Authentication, real payment gateway, concurrency, UI-specific behavior

**Next Milestone**: All tests green before starting Slice 0 (containerization and baseline metrics).

---

**Document Version**: 1.0  
**Last Updated**: 2025-01-20  
**Owner**: Development Team  
**Review Cycle**: After each migration slice
