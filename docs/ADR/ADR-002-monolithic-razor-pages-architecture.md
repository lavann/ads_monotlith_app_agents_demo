# ADR-002: Monolithic Razor Pages Architecture

## Status
Accepted (with planned evolution)

## Context
The application needed to deliver a complete e-commerce experience (product browsing, cart management, checkout, order tracking) with:

- Server-side rendering for SEO and accessibility
- Rapid development without separate frontend/backend projects
- Minimal JavaScript requirements
- Clear page-based navigation model
- Ease of onboarding for .NET developers

The system is explicitly designed as a "monolith before decomposition" to demonstrate modernisation patterns, meaning the architecture should support future extraction of services while remaining functional as a single deployment unit.

## Decision
We will implement the application using **ASP.NET Core 8 Razor Pages** in a **monolithic architecture** with layered organization.

### Architecture Components

1. **Presentation Layer: Razor Pages**
   - Server-side rendered HTML with embedded C# (`.cshtml` files)
   - Page models (`.cshtml.cs`) handle HTTP requests and business logic orchestration
   - Pages organized by feature: `/Products`, `/Cart`, `/Checkout`, `/Orders`
   - Form submission via POST handlers (`OnPostAsync`)

2. **Business Logic Layer: Services**
   - Interface-based services: `ICartService`, `ICheckoutService`, `IPaymentGateway`
   - Registered as scoped dependencies in DI container
   - Encapsulate domain operations and database interactions

3. **Data Access Layer: EF Core**
   - `AppDbContext` manages all database operations
   - Models represent both entities and view projections

4. **Supplementary APIs: Minimal APIs**
   - Two REST endpoints for potential frontend decoupling:
     - `POST /api/checkout`
     - `GET /api/orders/{id}`
   - Defined inline in `Program.cs`

### Deployment Model
- Single process (ASP.NET Core Kestrel)
- All components in one assembly (`RetailMonolith.csproj`)
- Stateless application (all state in database)

## Consequences

### Positive
- **Simple deployment**: Single executable, no distributed systems complexity
- **Easy debugging**: All code in one process, single breakpoint session
- **Transaction consistency**: ACID transactions via EF Core DbContext
- **Rapid development**: No API contracts, no serialization overhead between layers
- **Minimal infrastructure**: One database connection, one web server
- **Clear code organization**: Pages map directly to URLs

### Negative
- **Scalability limits**: Single instance bottleneck, no independent scaling of components
- **Technology coupling**: All modules must use same .NET version, same dependencies
- **Deployment coupling**: Any change requires full application redeployment
- **Team coordination**: Multiple developers may conflict on same codebase areas
- **Resource contention**: CPU-intensive checkout competes with UI rendering
- **Testing complexity**: Difficult to test components in isolation (though services have interfaces)

### Tradeoffs Observed in Code

**Hybrid Approach (Monolith + API Endpoints)**:
- Razor Pages for user-facing flows (majority)
- Minimal APIs for programmatic access (future clients)
- This suggests awareness of eventual API-first architecture

**Service Layer Abstraction**:
- Interfaces (`ICartService`, `ICheckoutService`) enable future extraction
- Current implementations tightly coupled to `AppDbContext`
- Services injected into both page models and API handlers

**Inconsistent Layering**:
- Some page models (Products, Orders) directly inject `AppDbContext`
- Others (Cart, Checkout) use services exclusively
- Indicates gradual evolution or incomplete refactoring

## Risks

### Short-Term
- **Duplicate logic**: Page models bypassing services (e.g., `Products/Index` has inline cart logic)
- **Tight coupling**: Services directly use EF Core entities, not DTOs
- **No API versioning**: Minimal APIs lack versioning strategy

### Long-Term
- **Extraction complexity**: Moving to microservices requires untangling database dependencies
- **Data consistency**: No event sourcing or saga patterns for distributed transactions
- **Session management**: Hardcoded "guest" user won't scale to multi-tenancy

## Mitigation Strategies (Evidence in Code)

1. **Service Interfaces**: All business logic behind interfaces, enabling swapping implementations
2. **Minimal APIs**: Already exposed, proving API consumption patterns
3. **Domain separation**: Clear boundaries (Products, Cart, Orders, Checkout) visible in folder structure
4. **Comments indicating future**: `// (future) publish events here` in `CheckoutService`

## Alternative Considered: API + SPA
**Rejected because**:
- Adds complexity of CORS, JWT authentication, API versioning
- Requires separate frontend build pipeline (npm, bundling)
- Slower initial development for demonstration purposes
- Still need server rendering for some scenarios (email templates, PDFs)

## Notes
The `README.md` explicitly states the app is "built to demonstrate modernisation and refactoring patterns" and is "ready for decomposition into microservices." This confirms the monolith is an intentional starting point, not the end state.

The presence of health check endpoint (`/health`) suggests preparation for container orchestration and monitoring, common in microservices.

## Related Decisions
- Future ADR: API Gateway pattern when extracting services
- Future ADR: Event-driven architecture for cross-service communication
- Future ADR: Strangler Fig pattern for gradual decomposition
