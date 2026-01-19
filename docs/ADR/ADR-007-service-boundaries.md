# ADR-007: Service Boundaries and Domain Decomposition

## Status
Accepted

## Context

The RetailMonolith application needs to be decomposed into microservices, but the choice of service boundaries is critical to success. Poor boundaries lead to:

- **Distributed monolith**: Services tightly coupled, must deploy together
- **Data coupling**: Services depend on each other's databases
- **Chatty communication**: Excessive network calls between services
- **Unclear ownership**: Multiple teams touch same service
- **Difficult testing**: Can't test services independently

The current monolith has implicit domain boundaries visible in code organization:
- **Products**: Catalog browsing, product information
- **Cart**: Shopping cart management
- **Checkout**: Payment processing, order creation
- **Orders**: Order history and tracking

However, the boundaries are not enforced (all code shares same database and can call any method).

**Key Questions**:
1. Which parts of the monolith should become separate services?
2. How fine-grained should services be (nano-services vs. larger services)?
3. Should we split by business capability or technical layer?
4. How do we handle cross-cutting concerns (inventory, payments)?

## Decision

We will decompose the monolith into **five bounded contexts** based on **business capabilities**, not technical layers.

### Service Boundaries

#### 1. Product Catalog Service

**Business Capability**: Product information management and discovery

**Responsibilities**:
- Serve product catalog (list, search, filter)
- Provide product details (name, description, price)
- Report inventory levels (read-only initially)

**Domain Entities**:
- `Product` (owns)
- `InventoryItem` (read-only view initially, owned by Inventory Service later)

**APIs**:
```
GET  /api/products                    # List all active products
GET  /api/products/{id}               # Get product details
GET  /api/products/{id}/inventory     # Check stock level
GET  /api/products?category={cat}     # Filter by category
```

**Data Ownership**:
- **Phase 1**: Products table (read/write), Inventory table (read-only)
- **Phase 2**: Products table only (Inventory Service owns Inventory table)

**Rationale**:
- ✅ **Read-heavy**: Products are browsed far more than updated (cache-friendly)
- ✅ **Independent updates**: Product descriptions change without affecting orders
- ✅ **Clear bounded context**: Products are a natural domain entity
- ✅ **Low coupling**: Other services only need product ID and name (can cache)
- ✅ **Scalability**: Can scale independently during high traffic (product launches)

**Team Ownership**: Catalog Team (2-3 developers)

---

#### 2. Cart Service

**Business Capability**: Shopping cart lifecycle management

**Responsibilities**:
- Create and retrieve carts for customers
- Add/remove/update items in cart
- Calculate cart totals
- Clear cart after checkout

**Domain Entities**:
- `Cart` (owns)
- `CartLine` (owns)

**APIs**:
```
GET    /api/carts/{customerId}                          # Get cart with items
POST   /api/carts/{customerId}/items                    # Add item to cart
PUT    /api/carts/{customerId}/items/{sku}             # Update quantity
DELETE /api/carts/{customerId}/items/{sku}             # Remove item
DELETE /api/carts/{customerId}                          # Clear cart
```

**Data Ownership**:
- **Phase 1**: Carts and CartLines tables (exclusive ownership)
- **Phase 2**: Redis (key-value store, TTL-based expiry)

**Rationale**:
- ✅ **Short-lived data**: Carts expire after 72 hours (ephemeral, low risk)
- ✅ **High write frequency**: Users constantly add/remove items (benefits from caching)
- ✅ **Clear bounded context**: Cart is isolated from products and orders
- ✅ **Well-encapsulated**: Already abstracted behind ICartService interface
- ✅ **Independent scaling**: Can scale during peak shopping hours

**Team Ownership**: Checkout Team (shares with Checkout Service)

---

#### 3. Checkout Service

**Business Capability**: Order processing and transaction orchestration

**Responsibilities**:
- Orchestrate checkout flow (cart → payment → order)
- Coordinate inventory reservation
- Process payments via payment gateway
- Publish domain events (OrderCreated, PaymentProcessed)
- Implement saga pattern for distributed transactions

**Domain Entities**:
- None (stateless orchestrator)
- Coordinates: Cart, Product, Inventory, Payment, Order

**APIs**:
```
POST /api/checkout                         # Process checkout
     Request: { customerId, paymentToken }
     Response: { orderId, status, total, paymentRef }
```

**Events Published**:
```
OrderCreated         → { orderId, customerId, total, items, timestamp }
PaymentProcessed     → { orderId, paymentRef, amount, status }
InventoryReserved    → { orderId, items: [{sku, qty}] }
CheckoutFailed       → { customerId, reason, cartSnapshot }
```

**External Dependencies**:
- **Cart Service**: Retrieve cart contents (`GET /api/carts/{customerId}`)
- **Product Catalog Service**: Validate product availability
- **Inventory Service** (future): Reserve stock
- **Payment Gateway**: Process payment (Stripe, PayPal)
- **Order Service**: Create order record (or publish event for Order Service to consume)

**Data Ownership**:
- None (no database, stateless)
- All state managed by dependent services

**Rationale**:
- ✅ **Orchestration responsibility**: Natural coordinator for complex multi-step flow
- ✅ **High business value**: Checkout is critical path for revenue
- ✅ **Event publisher**: Broadcasts state changes to interested parties (analytics, email)
- ✅ **Saga pattern**: Demonstrates distributed transaction handling
- ✅ **Stateless**: Easy to scale (no sticky sessions)

**Saga Pattern** (compensating transactions):
```
Step 1: Retrieve cart          → Compensate: N/A
Step 2: Reserve inventory      → Compensate: Release inventory
Step 3: Process payment        → Compensate: Refund payment (future)
Step 4: Create order           → Compensate: Cancel order (future)
Step 5: Publish OrderCreated   → Compensate: Publish OrderCancelled
Step 6: Clear cart             → Compensate: Restore cart (future)
```

**Team Ownership**: Checkout Team (2-3 developers)

---

#### 4. Order Service

**Business Capability**: Order history, tracking, and retrieval

**Responsibilities**:
- Store and retrieve order records
- Provide order history for customers
- Provide order details (line items, totals, status)
- Update order status (future: shipped, delivered)

**Domain Entities**:
- `Order` (owns)
- `OrderLine` (owns)

**APIs**:
```
GET  /api/orders?customerId={id}           # List customer orders
GET  /api/orders/{orderId}                 # Get order details
GET  /api/orders/{orderId}/status          # Get order status
PATCH /api/orders/{orderId}/status         # Update order status (future)
```

**Events Consumed** (future):
```
OrderCreated      → Create order record in local database
PaymentProcessed  → Update order payment status
OrderShipped      → Update order status to "Shipped"
```

**Data Ownership**:
- **Phase 1**: Orders and OrderLines tables (read/write)
- **Phase 2**: Dedicated Orders database (event-sourced, append-only)

**Rationale**:
- ✅ **Read-mostly**: Orders queried frequently, updated rarely (append-only pattern)
- ✅ **Clear bounded context**: Orders are historical records, isolated from active carts
- ✅ **Cacheable**: Order details don't change after creation (immutable)
- ✅ **Independent scaling**: Can scale read replicas for order history queries
- ✅ **Reporting**: Future analytics/reporting services consume OrderCreated events

**Team Ownership**: Order Management Team (2 developers)

---

#### 5. Web BFF (Backend for Frontend)

**Business Capability**: User interface rendering and session management

**Responsibilities**:
- Render HTML pages (Razor Pages)
- Aggregate data from multiple services (Product Catalog + Inventory for product listing)
- Manage user sessions (replace "guest" with session-based IDs)
- Handle authentication and authorization (future)
- Provide friendly error messages (hide service failures from users)

**Pages**:
- Home (`/`)
- Products listing (aggregates Product Catalog API)
- Cart view (calls Cart Service API)
- Checkout form (calls Checkout Service API)
- Order history (calls Order Service API)
- Order details (calls Order Service API)

**APIs Called**:
- Product Catalog Service: `GET /api/products`
- Cart Service: `GET /api/carts/{customerId}`, `POST /api/carts/{customerId}/items`
- Checkout Service: `POST /api/checkout`
- Order Service: `GET /api/orders?customerId={id}`, `GET /api/orders/{id}`

**Data Ownership**:
- None (no database, proxies to services)
- Session state (future: Redis or database-backed sessions)

**Rationale**:
- ✅ **Preserves existing UI**: Razor Pages remain, no frontend rewrite
- ✅ **Thin layer**: No business logic, just presentation and aggregation
- ✅ **User-focused**: Hides complexity of microservices from end users
- ✅ **Flexibility**: Can add SPA or mobile app later without duplicating logic
- ✅ **Error handling**: Circuit breakers and fallbacks prevent cascading failures

**Team Ownership**: Frontend Team (2-3 developers)

---

### Cross-Cutting Concerns

#### Inventory Management

**Current State**: Inventory table directly accessed by Checkout Service

**Phase 1 (Interim)**:
- Checkout Service directly queries/updates Inventory table
- Product Catalog Service reads Inventory (via database view or API)

**Phase 2 (Target)**:
- **Inventory Service** (dedicated microservice)
- Responsibilities: Reserve stock, release stock, replenish stock
- APIs: `POST /api/inventory/{sku}/reserve`, `POST /api/inventory/{sku}/release`
- Events: `InventoryReserved`, `InventoryReleased`, `OutOfStock`

**Rationale for Delay**:
- Inventory tightly coupled to Checkout (complex distributed transaction)
- Extract after Checkout Service stabilized (reduce risk)
- Learn from Checkout saga pattern first

#### Payment Processing

**Current State**: IPaymentGateway interface, MockPaymentGateway implementation

**Phase 1 (Interim)**:
- Checkout Service directly calls Payment Gateway (Stripe, PayPal)
- Replace MockPaymentGateway with real implementation (e.g., StripePaymentGateway)

**Phase 2 (Future, if needed)**:
- **Payment Service** (dedicated microservice)
- Responsibilities: Process payments, handle webhooks, manage refunds
- Useful if: Multiple payment providers, complex payment flows (subscriptions, split payments)

**Rationale for Keeping in Checkout Service**:
- Payment is core to checkout flow (not reused elsewhere)
- Payment Gateway already abstracted behind interface (swappable)
- Checkout Service already handles saga pattern (compensating transactions)

#### Authentication and Authorization

**Current State**: Hardcoded "guest" customer ID (no authentication)

**Phase 1 (Interim)**:
- Web BFF generates session-based customer IDs (Guid per session)
- Services accept customer ID as parameter (trust Web BFF)

**Phase 2 (Target)**:
- Web BFF implements ASP.NET Core Identity (user registration/login)
- Web BFF issues JWT tokens to services
- Services validate JWT tokens (shared signing key)

**Rationale**:
- Authentication is cross-cutting (all services need customer ID)
- Centralize in Web BFF (single point of authentication)
- Services remain stateless (no session management per service)

---

## Service Size Guidelines

**"Right-sized" Services** (not too large, not too small):

| Service | Lines of Code | Entities | Endpoints | Team Size |
|---------|---------------|----------|-----------|-----------|
| **Product Catalog** | ~500 | 2 | 4 | 2-3 |
| **Cart** | ~400 | 2 | 5 | 2-3 |
| **Checkout** | ~600 | 0 | 1 | 2-3 |
| **Order** | ~400 | 2 | 3 | 2 |
| **Web BFF** | ~800 | 0 | 0 (pages) | 2-3 |

**Guidelines**:
- ✅ **Single team ownership**: One team can understand entire service
- ✅ **Deployable in < 5 minutes**: Build, test, deploy cycle fast
- ✅ **Testable in isolation**: Can test without all services running
- ✅ **Clear bounded context**: Domain boundaries align with DDD principles

**Anti-Patterns to Avoid**:
- ❌ **Nano-services**: Services with single method (too fine-grained, high overhead)
- ❌ **Anemic services**: Services with no business logic (just CRUD wrappers)
- ❌ **Distributed monolith**: Services all depend on each other (must deploy together)

---

## Data Ownership

**Principle**: Each service exclusively owns its data (no direct database access from other services)

### Phase 1: Shared Database (Interim)

**Approach**: All services connect to same database, but respect ownership

| Service | Tables Owned | Tables Read-Only |
|---------|--------------|------------------|
| **Product Catalog** | Products | Inventory |
| **Cart** | Carts, CartLines | None |
| **Checkout** | None | Products, Inventory (temporarily) |
| **Order** | Orders, OrderLines | None |

**Enforcement**:
- **Database permissions** (each service has dedicated database user with limited grants):
  ```sql
  -- Product Catalog Service user
  CREATE USER [product-catalog-svc] WITH PASSWORD = '***';
  GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.Products TO [product-catalog-svc];
  GRANT SELECT ON dbo.Inventory TO [product-catalog-svc]; -- Read-only
  
  -- Cart Service user
  CREATE USER [cart-svc] WITH PASSWORD = '***';
  GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.Carts TO [cart-svc];
  GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.CartLines TO [cart-svc];
  
  -- Order Service user
  CREATE USER [order-svc] WITH PASSWORD = '***';
  GRANT SELECT, INSERT, UPDATE ON dbo.Orders TO [order-svc];
  GRANT SELECT, INSERT, UPDATE ON dbo.OrderLines TO [order-svc];
  ```
- **Database schemas** (separate schema per service for stricter isolation):
  ```sql
  CREATE SCHEMA catalog;
  CREATE SCHEMA cart;
  CREATE SCHEMA orders;
  
  -- Move tables to schemas
  ALTER SCHEMA catalog TRANSFER dbo.Products;
  ALTER SCHEMA cart TRANSFER dbo.Carts;
  ```
- **Code reviews** (enforce no cross-table joins, no foreign keys across services)
- **Integration tests** (verify services only access owned tables)
- **Database views** (for read-only cross-service data):
  ```sql
  -- View for Checkout Service to read product prices
  CREATE VIEW checkout.ProductPrices AS
  SELECT Id, Sku, Price FROM catalog.Products WHERE IsActive = 1;
  GRANT SELECT ON checkout.ProductPrices TO [checkout-svc];
  ```

---

### Phase 2: Database-per-Service (Target)

**Approach**: Each service has dedicated database instance

| Service | Database | Technology |
|---------|----------|------------|
| **Product Catalog** | ProductCatalog DB | SQL Server |
| **Cart** | Carts DB | Redis (key-value store) |
| **Checkout** | None | Stateless |
| **Order** | Orders DB | SQL Server (append-only) |

**Data Replication**:
- Services cache reference data (e.g., Order Service caches product names)
- Event-driven updates (OrderCreated event includes product names at order time)
- Eventual consistency accepted (product name change doesn't affect old orders)

---

## Communication Patterns

### Synchronous (HTTP/REST)

**When to Use**:
- Request-response patterns
- User-initiated actions (add to cart, checkout)
- Immediate feedback required

**Examples**:
```
Web BFF → Product Catalog: GET /api/products
Web BFF → Cart Service:    POST /api/carts/{customerId}/items
Checkout → Cart Service:   GET /api/carts/{customerId}
Checkout → Payment Gateway: POST /charges
```

**Resilience**:
- Circuit breakers (Polly)
- Retries with exponential backoff
- Timeouts (5s default, 30s for checkout)

---

### Asynchronous (Events)

**When to Use**:
- Fire-and-forget operations
- Eventual consistency acceptable
- Broadcasting state changes to multiple consumers

**Examples**:
```
Checkout Service publishes:
  - OrderCreated → Order Service, Email Service, Analytics
  - PaymentProcessed → Order Service, Fraud Detection
  - InventoryReserved → Order Service, Reporting

Future:
  Cart Service publishes:
    - CartAbandoned → Marketing (retargeting emails)
  Order Service publishes:
    - OrderShipped → Notification Service (SMS/email)
```

**Technology**: Azure Service Bus or RabbitMQ

---

## Service Dependencies

**Dependency Graph** (arrows = calls or subscribes to):

```
           ┌──────────────┐
           │   Web BFF    │
           └───────┬──────┘
                   │
        ┌──────────┼──────────┐
        │          │          │
        ▼          ▼          ▼
┌──────────┐ ┌──────────┐ ┌──────────┐
│ Product  │ │   Cart   │ │  Order   │
│ Catalog  │ │ Service  │ │ Service  │
└──────────┘ └──────────┘ └──────────┘
                   ▲          ▲
                   │          │
             ┌─────┴──────────┴─────┐
             │  Checkout Service    │
             └──────────────────────┘
                      │
                      ▼
              ┌──────────────┐
              │   Payment    │
              │   Gateway    │
              └──────────────┘

Event Bus (Async):
  OrderCreated → Order Service, Email Service, Analytics
  PaymentProcessed → Order Service
```

**Key Observations**:
- ✅ **Web BFF is facade**: User traffic enters via Web BFF only
- ✅ **Checkout is orchestrator**: Coordinates Cart, Payment, Order
- ✅ **No circular dependencies**: Services form DAG (directed acyclic graph)
- ✅ **Product Catalog independent**: No dependencies on other services

---

## Tradeoffs and Consequences

### Positive

✅ **Clear ownership**: Each team owns a service (faster decision-making)
✅ **Independent deployability**: Deploy Product Catalog without touching Cart
✅ **Independent scalability**: Scale Cart Service 10x during Black Friday
✅ **Technology flexibility**: Use Redis for Cart, SQL for Orders
✅ **Fault isolation**: Product Catalog crash doesn't affect Checkout
✅ **Testability**: Can test Cart Service in isolation (mock Product Catalog)

### Negative

⚠️ **Increased latency**: In-process call becomes HTTP call (adds 10-50ms)
⚠️ **Eventual consistency**: Product name change not immediately reflected in orders
⚠️ **Operational complexity**: More services to deploy, monitor, troubleshoot
⚠️ **Network fallacies**: Distributed systems are inherently unreliable (handle failures)
⚠️ **Transaction boundaries**: Can't use database transactions across services (use sagas)

### Risks

| Risk | Mitigation |
|------|------------|
| **Service boundaries change** | Accept refactoring cost, but minimize via DDD analysis upfront |
| **Too many services** | Start with 4-5 services, split further only if needed (measured decision) |
| **Data duplication** | Accept controlled duplication (e.g., order caches product name at checkout time) |
| **Integration complexity** | Strong API contracts, consumer-driven contract tests |

---

## Validation

**Domain-Driven Design (DDD) Alignment**:
- ✅ **Bounded contexts**: Each service maps to a DDD bounded context
- ✅ **Ubiquitous language**: Product, Cart, Order, Checkout are domain terms
- ✅ **Aggregate roots**: Product, Cart, Order are natural aggregates
- ✅ **Domain events**: OrderCreated, PaymentProcessed model real-world events

**Conway's Law Consideration**:
> "Organizations design systems that mirror their communication structure"

**Team Structure** (recommended):
- **Catalog Team**: Product Catalog Service
- **Checkout Team**: Cart Service + Checkout Service (shared domain)
- **Order Management Team**: Order Service
- **Frontend Team**: Web BFF
- **Platform Team**: Kubernetes, observability, CI/CD (supports all teams)

---

## Alternatives Considered

### Alternative 1: Single API Service

**Approach**: Monolith with single API layer (all business logic in one service)

**Rejected**: Doesn't achieve independent deployability or scalability

---

### Alternative 2: Technical Layer Split

**Approach**: Separate services for Controllers, Services, Data Access

**Rejected**: High coupling (all layers must deploy together), no business value

---

### Alternative 3: More Fine-Grained Services

**Example**: Separate "Product List Service", "Product Detail Service", "Product Search Service"

**Rejected**: Too fine-grained, excessive network overhead, minimal benefit

---

### Alternative 4: Fewer Services

**Example**: Combine Cart and Checkout into "Shopping Service"

**Rejected**: Cart is ephemeral (benefits from Redis), Checkout is orchestrator (stateless), different lifecycle and scaling needs

---

## Future Evolution

**Potential Service Additions** (after initial migration):

1. **Inventory Service** (Phase 3)
   - Extract from Checkout Service
   - Manage stock levels, reservations, replenishment

2. **Notification Service** (Phase 4)
   - Send order confirmation emails
   - Send SMS for shipping updates
   - Consume OrderCreated, OrderShipped events

3. **Recommendation Service** (Phase 5)
   - "Customers who bought X also bought Y"
   - Consume OrderCreated events for ML training

4. **Search Service** (Phase 6)
   - Full-text search, faceted search
   - Separate from Product Catalog for performance

**Principle**: Add services based on measured need, not speculation

---

## Related Decisions

- **ADR-005**: Strangler Fig Pattern - How to incrementally extract these services
- **ADR-006**: Kubernetes and Containers - How to deploy these services
- **ADR-008**: Database Migration Strategy - How to split shared database
- **ADR-009**: Saga Pattern for Checkout - How Checkout Service orchestrates distributed transactions

## References

- [Domain-Driven Design (Eric Evans)](https://www.domainlanguage.com/ddd/)
- [Microservices Patterns (Chris Richardson)](https://microservices.io/patterns/)
- [Building Microservices (Sam Newman)](https://www.oreilly.com/library/view/building-microservices-2nd/9781492034018/)
- [Bounded Context (Martin Fowler)](https://martinfowler.com/bliki/BoundedContext.html)

## Review and Approval

**Reviewed by**: Domain Experts, Architecture Team, Engineering Leads
**Approved by**: CTO, VP Engineering
**Date**: 2025-01-19

**Decision**: Proceed with 5-service decomposition: Product Catalog, Cart, Checkout, Order, Web BFF
