# ADR-004: Guest-Based Session Management

## Status
Accepted (Temporary/Demo)

## Context
E-commerce applications need to identify users across multiple HTTP requests to maintain shopping cart state and associate orders with customers. Traditional approaches include:

1. **Authentication-based**: Users register/login, receive authenticated session
2. **Cookie-based anonymous sessions**: Unique session ID stored in cookies
3. **Token-based**: JWT or similar tokens passed with each request
4. **IP-based**: Track users by IP address (unreliable)

The RetailMonolith application needed a customer identification strategy that would:

- Enable cart and order functionality without authentication
- Simplify initial development and demonstration
- Allow the application to be used immediately without account creation
- Support future addition of proper user management

The application is designed as a demonstration system to showcase architecture patterns, not as a production-ready e-commerce platform.

## Decision
We will use a **hardcoded "guest" customer identifier** for all operations, with no session or authentication mechanisms.

### Implementation

**Universal Customer ID**: All services and page models use the literal string `"guest"` as the customer identifier.

**Evidence in Code**:

1. **Service Layer**:
   ```csharp
   // CartService calls
   await _cartService.AddToCartAsync("guest", productId);
   var cart = await _cartService.GetCartWithLinesAsync("guest");
   await _checkoutService.CheckoutAsync("guest", paymentToken);
   ```

2. **Data Models**:
   ```csharp
   public class Cart
   {
       public string CustomerId { get; set; } = "guest";
   }
   
   public class Order
   {
       public string CustomerId { get; set; } = "guest";
   }
   ```

3. **Page Models**:
   - Products/Index: `_cartService.AddToCartAsync("guest", productId)`
   - Cart/Index: `_cartService.GetCartWithLinesAsync("guest")`
   - Checkout/Index: `_checkoutService.CheckoutAsync("guest", PaymentToken)`

4. **Minimal APIs**:
   ```csharp
   app.MapPost("/api/checkout", async (ICheckoutService svc) =>
   {
       var order = await svc.CheckoutAsync("guest", "tok_test");
       // ...
   });
   ```

### Behavior

- **Single Shared Cart**: All users share the same shopping cart
- **Single Order History**: All orders appear in the same order list
- **No Privacy**: Anyone can view anyone's orders (since everyone is "guest")
- **No Personalization**: No saved addresses, preferences, or payment methods

## Consequences

### Positive
- **Zero barrier to entry**: Users can immediately browse and shop without signup
- **Simplified development**: No authentication middleware, no user tables, no password hashing
- **Fast prototyping**: Focus on business logic rather than identity management
- **Easy testing**: Predictable customer ID simplifies automated tests
- **No session state**: Application remains stateless (all state in database)

### Negative
- **No multi-user support**: Impossible to distinguish between different users
- **Data conflicts**: Multiple concurrent users will interfere with each other's carts
- **Privacy violation**: All orders visible to all users
- **No personalization**: Can't save user preferences or history
- **Security gap**: No authorization checks (anyone can access any data)
- **Not production-ready**: Fundamentally unsuitable for real-world use

### Risks

1. **Concurrent User Conflicts**
   - **Scenario**: User A adds item, User B adds different item, both see combined cart
   - **Impact**: Confusing UX, potential accidental purchases
   - **Likelihood**: High if multiple people use the app simultaneously

2. **Order History Leakage**
   - **Scenario**: All orders visible at `/Orders` regardless of who placed them
   - **Impact**: Privacy violation, exposure of purchase history
   - **Current State**: Orders/Index loads all orders without filtering

3. **Cart Collision**
   - **Scenario**: User A clears cart, affecting User B's session
   - **Impact**: Lost cart contents, frustrated users
   - **Code Path**: `CartService.ClearCartAsync("guest")` removes the shared cart

4. **No Audit Trail**
   - **Scenario**: Unable to determine which user performed which action
   - **Impact**: No accountability, no fraud detection, no customer support

## Alternatives Considered

### 1. Cookie-Based Anonymous Sessions
**Pros**: Each browser gets unique cart, no login required
**Cons**: Requires session middleware, cookie management
**Why not chosen**: Added complexity for demo app

### 2. Local Storage (Client-Side Cart)
**Pros**: No server state, scales infinitely
**Cons**: Lost on browser change, requires JavaScript, security concerns
**Why not chosen**: Razor Pages server-side rendering model

### 3. ASP.NET Core Identity
**Pros**: Full-featured authentication/authorization
**Cons**: Complex setup, user management UI needed
**Why not chosen**: Out of scope for demonstration

### 4. Azure AD B2C
**Pros**: Managed identity service, social login
**Cons**: External dependency, configuration complexity
**Why not chosen**: Overkill for prototype

## Temporary Nature

**Comments in code confirm this is a stopgap**:
```csharp
// For simplicity, using a hardcoded customer ID
// In a real application, this would come from the authenticated user context
// or session
```
(From `Pages/Checkout/Index.cshtml.cs`)

This comment acknowledges the limitation and indicates awareness of the proper solution.

## Migration Path to Production

### Phase 1: Anonymous Sessions (Quick Fix)
```csharp
// Generate session ID on first visit
var sessionId = HttpContext.Session.GetString("CustomerId");
if (string.IsNullOrEmpty(sessionId))
{
    sessionId = Guid.NewGuid().ToString();
    HttpContext.Session.SetString("CustomerId", sessionId);
}
```
- Minimal code change
- Isolated carts per browser session
- Still anonymous (no login)

### Phase 2: Optional Authentication
```csharp
var customerId = User.Identity.IsAuthenticated 
    ? User.FindFirstValue(ClaimTypes.NameIdentifier)
    : HttpContext.Session.GetString("CustomerId");
```
- Guest checkout + registered users
- Carts persist across devices for logged-in users
- Requires ASP.NET Core Identity setup

### Phase 3: Full User Management
- Registration/login flows
- Email verification
- Password reset
- Profile management
- OAuth providers (Google, Facebook)

### Code Refactoring Required
All instances of `"guest"` must be replaced with dynamic customer ID resolution:

**Files to update**:
- Pages/Products/Index.cshtml.cs (1 occurrence)
- Pages/Cart/Index.cshtml.cs (1 occurrence)
- Pages/Checkout/Index.cshtml.cs (1 occurrence)
- Services/CartService.cs (method signatures accept `customerId`)
- Services/CheckoutService.cs (method signatures accept `customerId`)
- Minimal API handlers in Program.cs (2 occurrences)

**Services are already parameterized**, so primary work is in page models and API handlers extracting customer ID from context.

## Testing Implications

**Current State**: 
- Easy to test (predictable customer ID)
- Tests don't need authentication setup
- All tests use same "guest" customer

**Future State**:
- Tests must mock `HttpContext.Session` or `User.Identity`
- Integration tests need separate test users
- Isolation required between test runs

## Related Decisions
- ADR-002: Monolithic architecture makes customer ID extraction consistent across all page models
- Future ADR: Authentication provider selection (Identity vs. AD B2C vs. custom)
- Future ADR: Cart persistence strategy (database vs. cache vs. client-side)

## Recommendation
⚠️ **Replace before production deployment**

Minimum acceptable solution: Anonymous session IDs (Phase 1 above)
Recommended solution: Optional authentication (Phase 2 above)
