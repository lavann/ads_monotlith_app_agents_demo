# Target Architecture: Containerized Microservices

## Executive Summary

This document defines the target architecture for migrating RetailMonolith from a monolithic ASP.NET Core application to a containerized microservices architecture. The target architecture maintains all current functionality while enabling independent scaling, deployment, and evolution of business capabilities.

**Key Principles:**
- **Service autonomy**: Each service owns its data and logic
- **Containerization**: All services run in Docker containers
- **Incremental migration**: Strangler Fig pattern - no big-bang rewrite
- **Behavioral preservation**: Existing functionality continues to work throughout migration
- **Technology continuity**: .NET 8, SQL Server, existing patterns maintained

---

## Service Boundaries

Based on domain analysis of the current monolith, we identify five service boundaries (four domain services plus one BFF):

### 1. Product Catalog Service

**Responsibility**: Product information and catalog management

**Domain Entities:**
- Product
- InventoryItem (read-only view)

**APIs:**
```
GET  /api/products                    # List all active products
GET  /api/products/{id}               # Get product details
GET  /api/products/{id}/inventory     # Check stock level
GET  /api/products?category={cat}     # Filter by category
```

**Data Ownership:**
- Products table (read/write)
- Inventory table (read-only until Slice 3)

**Current Monolith Mapping:**
- Pages/Products/Index.cshtml → Web BFF
- Direct DbContext access → Product Catalog Service API

**Rationale:**
- Products are read-heavy, benefit from independent caching
- Product catalog changes rarely affect other domains
- Inventory reads needed by checkout, but writes can stay centralized initially

---

### 2. Cart Service

**Responsibility**: Shopping cart lifecycle management

**Domain Entities:**
- Cart
- CartLine

**APIs:**
```
GET    /api/carts/{customerId}                          # Get cart with items
POST   /api/carts/{customerId}/items                    # Add item to cart
PUT    /api/carts/{customerId}/items/{sku}             # Update quantity
DELETE /api/carts/{customerId}/items/{sku}             # Remove item
DELETE /api/carts/{customerId}                          # Clear cart
```

**Data Ownership:**
- Carts table (exclusive ownership)
- CartLines table (exclusive ownership)

**Current Monolith Mapping:**
- Services/CartService.cs → Cart Service
- Pages/Cart/Index.cshtml → Web BFF

**Rationale:**
- Cart is short-lived, high-write domain
- Clear bounded context with minimal external dependencies
- Natural candidate for caching (Redis future optimization)
- Well-encapsulated behind existing ICartService interface

---

### 3. Order Service

**Responsibility**: Order history, tracking, and retrieval

**Domain Entities:**
- Order
- OrderLine

**APIs:**
```
GET  /api/orders?customerId={id}           # List customer orders
GET  /api/orders/{orderId}                 # Get order details
GET  /api/orders/{orderId}/status          # Get order status
```

**Data Ownership:**
- Orders table (read/write)
- OrderLines table (read/write)

**Current Monolith Mapping:**
- Pages/Orders → Web BFF
- Direct DbContext queries → Order Service API

**Rationale:**
- Orders are read-mostly after creation
- Clear query patterns (by customer, by order ID)
- No complex business logic, primarily CRUD
- Can be optimized independently (read replicas, caching)

---

### 4. Checkout Service

**Responsibility**: Order creation, payment processing, inventory reservation

**Domain Entities:**
- Order (creates)
- Payment transactions (coordinates)
- Inventory reservations (coordinates)

**APIs:**
```
POST /api/checkout                         # Process checkout
     Request: { customerId, paymentToken }
     Response: { orderId, status, total, paymentRef }
```

**Events Published:**
```
OrderCreated         → { orderId, customerId, total, timestamp }
PaymentProcessed     → { orderId, paymentRef, amount, status }
InventoryReserved    → { orderId, items: [{sku, qty}] }
CheckoutFailed       → { customerId, reason, cartSnapshot }
```

**External Dependencies:**
- **Cart Service**: Retrieve cart contents
- **Product Catalog Service**: Validate product availability
- **Inventory Service** (future): Reserve stock
- **Payment Gateway**: Process payment
- **Order Service**: Create order record

**Current Monolith Mapping:**
- Services/CheckoutService.cs → Checkout Service
- Pages/Checkout/Index.cshtml → Web BFF

**Rationale:**
- Orchestrates complex multi-step workflow
- Natural coordinator for distributed transaction (saga pattern)
- Already abstracted behind ICheckoutService interface
- High-value target for reliability improvements (retries, circuit breakers)

---

### 5. Web BFF (Backend for Frontend)

**Responsibility**: Server-side rendering and user session management

**Technology**: ASP.NET Core Razor Pages (existing)

**Pages:**
- Home (`/`)
- Products listing (aggregates Product Catalog API)
- Cart view (calls Cart Service API)
- Checkout form (calls Checkout Service API)
- Order history (calls Order Service API)
- Order details (calls Order Service API)

**Responsibilities:**
- Session management (future: replace "guest" with session IDs)
- Authentication/authorization (future)
- HTML rendering
- API aggregation and orchestration
- Error handling and user-friendly messages

**Rationale:**
- Preserves existing Razor Pages UI
- Thin layer - no business logic
- Translates between user interactions and service APIs
- Allows future addition of SPA or mobile app without duplicating logic

---

## Target Deployment Architecture

### Container Platform

**Runtime**: Docker containers orchestrated by **Kubernetes**

**Rationale:**
- Industry standard for container orchestration
- Built-in service discovery, load balancing, health checks
- Declarative configuration (Infrastructure as Code)
- Supports both cloud (AKS, EKS, GKE) and on-premises
- .NET 8 has excellent container support

### Deployment Topology

```
┌─────────────────────────────────────────────────────────────────────────┐
│                           Kubernetes Cluster                             │
│                                                                           │
│  ┌────────────────────────────────────────────────────────────────────┐ │
│  │                        Ingress Controller                           │ │
│  │              (NGINX / Traefik / Azure App Gateway)                  │ │
│  │                   TLS Termination / Routing                         │ │
│  └───────┬────────────────────────────────────────────────────────────┘ │
│          │                                                                │
│          ├─────────────► Web BFF (3 replicas)                            │
│          │                Razor Pages, Session                           │
│          │                Port: 8080                                      │
│          │                                                                │
│          └─────────────► API Gateway (optional, future)                  │
│                           OAuth2, Rate Limiting                          │
│                                                                           │
│  ┌──────────────────────────────────────────────────────────────────┐   │
│  │                    Microservices (Internal)                       │   │
│  │                                                                    │   │
│  │  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐  │   │
│  │  │ Product Catalog │  │  Cart Service   │  │  Order Service  │  │   │
│  │  │   Service       │  │                 │  │                 │  │   │
│  │  │  (3 replicas)   │  │  (3 replicas)   │  │  (2 replicas)   │  │   │
│  │  │  Port: 8081     │  │  Port: 8082     │  │  Port: 8084     │  │   │
│  │  └────────┬────────┘  └────────┬────────┘  └────────┬────────┘  │   │
│  │           │                    │                    │            │   │
│  │           └────────────────────┴────────────────────┘            │   │
│  │                              │                                   │   │
│  │                    ┌─────────┴────────┐                          │   │
│  │                    │ Checkout Service │                          │   │
│  │                    │   (2 replicas)   │                          │   │
│  │                    │   Port: 8083     │                          │   │
│  │                    └─────────┬────────┘                          │   │
│  │                              │                                   │   │
│  └──────────────────────────────┼───────────────────────────────────┘   │
│                                 │                                        │
│  ┌──────────────────────────────┼───────────────────────────────────┐   │
│  │              External Service Integrations                        │   │
│  │                              │                                    │   │
│  │                    ┌─────────┴────────┐                           │   │
│  │                    │ Payment Gateway  │                           │   │
│  │                    │   (External)     │                           │   │
│  │                    └──────────────────┘                           │   │
│  └────────────────────────────────────────────────────────────────────┘  │
│                                                                           │
│  ┌────────────────────────────────────────────────────────────────────┐ │
│  │                        Data Layer                                  │ │
│  │                                                                     │ │
│  │  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐  ┌────────┐│ │
│  │  │   Products   │  │    Carts     │  │    Orders    │  │ Shared ││ │
│  │  │   Database   │  │   Database   │  │   Database   │  │  DB*   ││ │
│  │  │  (SQL)       │  │  (SQL/Redis) │  │  (SQL)       │  │ (SQL)  ││ │
│  │  └──────────────┘  └──────────────┘  └──────────────┘  └────────┘│ │
│  │                                                          *Phase 1  │ │
│  └────────────────────────────────────────────────────────────────────┘ │
│                                                                           │
│  ┌────────────────────────────────────────────────────────────────────┐ │
│  │                    Observability Stack                             │ │
│  │                                                                     │ │
│  │    Prometheus (Metrics) │ Grafana (Dashboards) │ Loki (Logs)      │ │
│  │    Jaeger (Distributed Tracing) │ Health Checks                   │ │
│  └────────────────────────────────────────────────────────────────────┘ │
└───────────────────────────────────────────────────────────────────────────┘
```

### Container Specifications

**Base Image**: `mcr.microsoft.com/dotnet/aspnet:8.0` (runtime)
**Build Image**: `mcr.microsoft.com/dotnet/sdk:8.0` (build)

**Resource Requests/Limits (per pod):**

| Service | CPU Request | CPU Limit | Memory Request | Memory Limit |
|---------|-------------|-----------|----------------|--------------|
| Web BFF | 200m | 1000m | 256Mi | 512Mi |
| Product Catalog | 100m | 500m | 128Mi | 256Mi |
| Cart Service | 100m | 500m | 128Mi | 256Mi |
| Order Service | 100m | 500m | 128Mi | 256Mi |
| Checkout Service | 200m | 1000m | 256Mi | 512Mi |

**Health Checks:**
- **Liveness**: `/health/live` (application running)
- **Readiness**: `/health/ready` (dependencies healthy, ready for traffic)
- **Startup**: `/health/startup` (initial database migration/seeding)

---

## Communication Patterns

### Synchronous Communication (HTTP/REST)

**When to Use:**
- Request-response patterns
- Immediate consistency required
- User-initiated actions

**Implementation:**
- RESTful JSON APIs
- HTTP/1.1 or HTTP/2
- Service-to-service calls via Kubernetes service DNS

**Pattern Examples:**
1. **Web BFF → Product Catalog**: User browsing products
2. **Web BFF → Cart Service**: User viewing cart
3. **Checkout Service → Cart Service**: Retrieve cart for checkout
4. **Checkout Service → Payment Gateway**: Process payment

**Resilience:**
- Circuit breaker pattern (Polly library already in dependencies)
- Retry with exponential backoff
- Timeout configuration (5s default, 30s for checkout)
- Fallback responses where appropriate

**Service Discovery:**
- Kubernetes DNS: `http://cart-service.default.svc.cluster.local:8082`
- Environment variables for service endpoints
- No client-side service discovery needed

---

### Asynchronous Communication (Events)

**When to Use:**
- Fire-and-forget operations
- Eventual consistency acceptable
- Decoupling producers from consumers
- Broadcasting state changes

**Technology**: **Azure Service Bus** (primary) or **RabbitMQ** (on-premises alternative)

**Message Broker Topology:**

```
                     ┌──────────────────────┐
                     │  Message Broker      │
                     │  (Azure Service Bus) │
                     └──────────┬───────────┘
                                │
           ┌────────────────────┼────────────────────┐
           │                    │                    │
      Topic: orders        Topic: payments    Topic: inventory
           │                    │                    │
    ┌──────┴──────┐      ┌──────┴──────┐     ┌──────┴──────┐
    │ OrderCreated│      │PaymentOK    │     │StockReserved│
    │ OrderShipped│      │PaymentFailed│     │StockReleased│
    └──────┬──────┘      └──────┬──────┘     └──────┬──────┘
           │                    │                    │
    Subscribers:          Subscribers:         Subscribers:
    - Email Service       - Checkout Svc       - Order Service
    - Analytics           - Order Service      - Reporting
    - Notification        - Fraud Detection    
```

**Event Schema (Cloud Events Standard):**
```json
{
  "specversion": "1.0",
  "type": "com.retailmonolith.order.created",
  "source": "/checkout-service",
  "id": "A234-1234-1234",
  "time": "2025-01-19T10:30:00Z",
  "datacontenttype": "application/json",
  "data": {
    "orderId": 12345,
    "customerId": "cust-789",
    "total": 99.99,
    "currency": "GBP",
    "items": [
      {"sku": "WIDGET-001", "quantity": 2, "price": 49.99}
    ]
  }
}
```

**Event Examples:**

1. **OrderCreated** (published by Checkout Service)
   - Consumed by: Order Service (persist order), Email Service (send confirmation), Analytics
   
2. **PaymentProcessed** (published by Checkout Service)
   - Consumed by: Order Service (update status), Fraud Detection, Accounting

3. **InventoryReserved** (published by Checkout Service initially, later by Inventory Service)
   - Consumed by: Order Service, Reporting

4. **CartAbandoned** (future: published by Cart Service)
   - Consumed by: Marketing (retargeting emails)

**Rationale for Events:**
- Checkout Service shouldn't block on email sending
- Analytics can process orders without coupling to Checkout
- Supports future features (notifications, fraud detection) without changing existing services
- Natural fit for saga orchestration in checkout flow

---

## Data Access Strategy

### Phase 1: Shared Database (Interim)

**Approach**: All services connect to the same SQL Server database

**Schema Ownership:**
- Product Catalog Service: Products, Inventory (read-only)
- Cart Service: Carts, CartLines (read/write)
- Order Service: Orders, OrderLines (read/write)
- Checkout Service: All tables (coordinator role)

**Access Control:**
- Each service has dedicated database user
- Grant only necessary permissions (e.g., Cart Service cannot modify Orders)
- Database views for cross-service reads

**Connection Strings** (stored in Azure Key Vault or Kubernetes Secrets):
```bash
# Example format only - NEVER store credentials in code or configuration files
Server=sql-server.database.windows.net;Database=RetailMonolith;User Id=cart-service;Password=***;

# Best Practice: Use Azure Key Vault or Kubernetes Secrets
# Kubernetes Secret:
kubectl create secret generic database-secret \
  --from-literal=connection-string="Server=...;Database=...;User Id=...;Password=..."

# Azure Key Vault (preferred for production):
# Store in Key Vault, fetch via Managed Identity at runtime
```

**Security Note**: Connection strings must always be stored in secure secret management systems (Azure Key Vault, Kubernetes Secrets), never in source code, configuration files, or environment variables visible in logs.

**Rationale:**
- Minimizes initial migration complexity
- Preserves transactional consistency during transition
- Allows service extraction without data migration
- Proven pattern (Shopify, GitHub used this approach)

**Risks:**
- Schema coupling (services must coordinate schema changes)
- Performance contention (shared database resources)
- Violates microservices data ownership principle

**Mitigation:**
- Enforce schema ownership via code reviews
- Monitor slow queries per service
- Plan database split for Phase 2

---

### Phase 2: Database-per-Service (Target)

**Approach**: Each service owns its dedicated database

```
┌─────────────────────────────────────────────────────────────────┐
│  Product Catalog Service  →  ProductCatalog Database            │
│    Tables: Products, Inventory                                  │
│    Technology: SQL Server (read replicas for scaling)           │
└─────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────┐
│  Cart Service  →  Carts Database                                │
│    Tables: Carts, CartLines                                     │
│    Technology: Redis (in-memory, TTL-based) or SQL Server       │
└─────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────┐
│  Order Service  →  Orders Database                              │
│    Tables: Orders, OrderLines                                   │
│    Technology: SQL Server (append-only, optimized for reads)    │
└─────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────┐
│  Checkout Service  →  No Database (stateless orchestrator)      │
│    Coordinates via API calls and events                         │
└─────────────────────────────────────────────────────────────────┘
```

**Data Migration Strategy:**
1. **Identify dependencies**: Map foreign key relationships and queries
2. **Introduce APIs**: Replace direct database joins with service calls
3. **Replicate data**: Use database replication or Change Data Capture (CDC)
4. **Cutover**: Switch services to new databases, decommission shared schema
5. **Archive**: Keep old database for rollback window (30 days)

**Cross-Service Data Access:**
- **No direct database access** between services
- Use APIs for real-time data (e.g., Checkout calls Product Catalog for price)
- Use events for eventual consistency (e.g., Order Service caches product names)
- Materialized views for reporting (separate analytics database)

**Referential Integrity:**
- **Application-level enforcement**: Services validate references via API calls
- **Eventual consistency**: Accept temporary inconsistency (e.g., product deleted but order references it)
- **Saga pattern**: Coordinate distributed transactions (checkout saga)

---

## Configuration Management

### Environment Variables (per service)

**Common:**
```bash
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://+:8080
LOG_LEVEL=Information
CORRELATION_ID_HEADER=X-Correlation-ID
```

**Service-Specific:**
```bash
# Cart Service
DATABASE_CONNECTION_STRING=Server=...;Database=Carts;...
REDIS_CONNECTION_STRING=redis-cache:6379,ssl=false
CART_EXPIRY_HOURS=72

# Checkout Service
CART_SERVICE_URL=http://cart-service:8082
PRODUCT_CATALOG_URL=http://product-catalog-service:8081
ORDER_SERVICE_URL=http://order-service:8084
PAYMENT_GATEWAY_URL=https://api.stripe.com
PAYMENT_API_KEY=sk_live_***
SERVICE_BUS_CONNECTION_STRING=Endpoint=sb://...
```

**Kubernetes ConfigMaps (non-sensitive):**
```yaml
apiVersion: v1
kind: ConfigMap
metadata:
  name: checkout-service-config
data:
  CART_SERVICE_URL: "http://cart-service:8082"
  TIMEOUT_SECONDS: "30"
  RETRY_COUNT: "3"
```

**Kubernetes Secrets (sensitive):**
```yaml
apiVersion: v1
kind: Secret
metadata:
  name: checkout-service-secrets
type: Opaque
data:
  DATABASE_CONNECTION_STRING: <base64-encoded>
  PAYMENT_API_KEY: <base64-encoded>
```

**External Secrets Management:**
- **Azure Key Vault** (preferred for Azure deployments)
- **HashiCorp Vault** (cloud-agnostic alternative)
- Secrets injected at runtime via CSI driver

---

## Routing and API Gateway

### Phase 1: Simple Ingress Routing

**Technology**: NGINX Ingress Controller

**Routing Rules:**
```yaml
# External traffic
https://retail.example.com/              → Web BFF (port 8080)
https://retail.example.com/products      → Web BFF (Razor Pages)
https://retail.example.com/cart          → Web BFF (Razor Pages)
https://retail.example.com/api/*         → Web BFF (proxies to services)

# Internal service-to-service (not exposed externally)
http://cart-service.default.svc.cluster.local:8082
http://product-catalog-service.default.svc.cluster.local:8081
```

**Rationale:**
- Preserve existing URL structure
- Web BFF acts as gateway for user traffic
- Services communicate directly (no gateway overhead)

---

### Phase 2: API Gateway (Future)

**Technology**: **Azure API Management** or **Kong Gateway**

**Responsibilities:**
- OAuth2 authentication (future user login)
- Rate limiting per customer
- API versioning (`/v1/api/products`)
- Request/response transformation
- Analytics and monitoring
- Developer portal (external API consumers)

**When to Introduce:**
- When exposing APIs to external partners or mobile apps
- When implementing authentication/authorization
- When requiring advanced rate limiting or throttling

**Not Needed Initially:**
- Web BFF handles user authentication
- Services trust internal network
- Minimal external API surface

---

## Security Architecture

### Network Security

**Kubernetes Network Policies:**
```yaml
# Example: Cart Service can only be called by Web BFF and Checkout Service
apiVersion: networking.k8s.io/v1
kind: NetworkPolicy
metadata:
  name: cart-service-ingress
spec:
  podSelector:
    matchLabels:
      app: cart-service
  ingress:
  - from:
    - podSelector:
        matchLabels:
          app: web-bff
    - podSelector:
        matchLabels:
          app: checkout-service
```

**TLS:**
- External traffic: TLS termination at ingress (Let's Encrypt certificates)
- Internal traffic: mTLS optional (Service Mesh future consideration)

---

### Authentication & Authorization

**Phase 1 (Migration):**
- Web BFF continues with session-based authentication
- Services trust requests from Web BFF (internal network)
- No service-to-service authentication

**Phase 2 (Secure):**
- Web BFF: ASP.NET Core Identity with JWT tokens
- Services: Validate JWT tokens (shared signing key)
- Service-to-service: Mutual TLS or API keys

**Authorization:**
- Web BFF enforces user permissions (view own orders only)
- Services enforce domain rules (cannot checkout empty cart)

---

## Observability and Monitoring

### Distributed Tracing

**Technology**: **Jaeger** (OpenTelemetry compatible)

**Implementation:**
- Each service propagates trace context (W3C Trace Context headers)
- Instrumentation via OpenTelemetry .NET SDK
- Visualize request flow: Web BFF → Checkout → Cart → Payment Gateway

**Example Trace:**
```
Request: POST /checkout
├─ Web BFF: CheckoutPageModel.OnPostAsync [50ms]
│  └─ HTTP: POST http://checkout-service:8083/api/checkout [450ms]
│     ├─ Checkout Service: CheckoutAsync [450ms]
│     │  ├─ HTTP: GET http://cart-service:8082/api/carts/guest [20ms]
│     │  ├─ HTTP: POST https://api.stripe.com/charges [380ms]
│     │  └─ HTTP: POST http://order-service:8084/api/orders [30ms]
│     └─ Message: OrderCreated published [10ms]
└─ Response: 302 Redirect to /orders/details?id=123
```

---

### Metrics

**Technology**: **Prometheus** (collection) + **Grafana** (dashboards)

**Key Metrics per Service:**
- HTTP request rate (requests/second)
- HTTP error rate (5xx responses)
- Request duration (p50, p95, p99)
- Database query duration
- Circuit breaker state (open/closed)
- Active connections

**Business Metrics:**
- Checkouts per minute
- Average order value
- Cart abandonment rate
- Payment success rate

**Dashboards:**
1. **Service Health**: Error rates, latencies, throughput
2. **Business KPIs**: Orders, revenue, conversion rates
3. **Infrastructure**: CPU, memory, disk, network per pod

---

### Logging

**Technology**: **Structured logging** (Serilog) → **Loki** or **Azure Application Insights**

**Log Levels:**
- **Debug**: Development only
- **Information**: Significant business events (order created, payment processed)
- **Warning**: Retryable errors, circuit breaker opened
- **Error**: Failures requiring attention (payment declined, database timeout)
- **Critical**: System failures (service crash, database unreachable)

**Log Enrichment:**
- **Correlation ID**: Track request across services
- **Customer ID**: Associate logs with user actions
- **Service name**: Identify log source
- **Environment**: Production vs. Staging

**Example Structured Log:**
```json
{
  "timestamp": "2025-01-19T10:30:45.123Z",
  "level": "Information",
  "message": "Order created successfully",
  "correlationId": "abc-123-def-456",
  "customerId": "cust-789",
  "orderId": 12345,
  "total": 99.99,
  "service": "checkout-service",
  "environment": "production"
}
```

---

### Health Checks

**ASP.NET Core Health Checks** (built-in)

**Endpoints:**
1. `/health/live`: Liveness probe (process running)
2. `/health/ready`: Readiness probe (database connected, dependencies healthy)
3. `/health/startup`: Startup probe (migrations complete, seeding done)

**Kubernetes Configuration:**
```yaml
livenessProbe:
  httpGet:
    path: /health/live
    port: 8080
  initialDelaySeconds: 10
  periodSeconds: 10

readinessProbe:
  httpGet:
    path: /health/ready
    port: 8080
  initialDelaySeconds: 5
  periodSeconds: 5
```

---

## Disaster Recovery and Rollback

### Deployment Strategy

**Blue-Green Deployments:**
- Deploy new version (green) alongside existing (blue)
- Route small percentage of traffic to green (canary)
- Monitor error rates and latencies
- If healthy, gradually shift 100% traffic to green
- Keep blue running for 24 hours (rollback window)

**Kubernetes Resources:**
- Use Deployment objects (not Pods directly)
- RollingUpdate strategy with `maxSurge: 1, maxUnavailable: 0`
- PodDisruptionBudgets to ensure availability during updates

---

### Rollback Procedures

**Application Rollback:**
```bash
# Instant rollback to previous version
kubectl rollout undo deployment/checkout-service
kubectl rollout status deployment/checkout-service
```

**Database Rollback:**
- **Forward-only migrations**: Never drop columns during migration phase
- **Backward-compatible changes**: Add columns as nullable initially
- **Data replication**: Keep shared database as fallback for 30 days

**Event Replay:**
- Messages retained in Service Bus for 7 days (configurable)
- Can replay events if consumer was down or had bug

---

## Technology Stack Summary

| Component | Technology | Version | Rationale |
|-----------|------------|---------|-----------|
| **Runtime** | .NET | 8.0 | Existing, excellent container support |
| **Framework** | ASP.NET Core | 8.0 | Existing, mature, high performance |
| **ORM** | Entity Framework Core | 9.0+ | Existing, familiar to team |
| **Database** | SQL Server | 2022 | Existing, mature, transactional |
| **Cache** (future) | Redis | 7.0+ | Cart Service optimization |
| **Message Broker** | Azure Service Bus | - | Managed, reliable, .NET SDK |
| **Container Runtime** | Docker | 24.0+ | Industry standard |
| **Orchestration** | Kubernetes | 1.28+ | Industry standard, cloud-agnostic |
| **API Resilience** | Polly | 8.0+ | Already in dependencies |
| **Tracing** | OpenTelemetry | 1.7+ | Vendor-neutral, cloud-native |
| **Metrics** | Prometheus | 2.48+ | De-facto standard for K8s |
| **Logging** | Serilog | 3.1+ | Structured logging, multiple sinks |
| **Secrets** | Azure Key Vault | - | Secure, audited, integrated with AKS |

---

## Non-Functional Requirements

### Performance

| Metric | Target | Measurement |
|--------|--------|-------------|
| **Product listing** | < 200ms p95 | Web BFF → Product Catalog |
| **Add to cart** | < 150ms p95 | Web BFF → Cart Service |
| **View cart** | < 100ms p95 | Web BFF → Cart Service |
| **Checkout** | < 3s p95 | Includes payment gateway latency |
| **View order** | < 200ms p95 | Web BFF → Order Service |

### Scalability

| Service | Min Replicas | Max Replicas | Scale Trigger |
|---------|--------------|--------------|---------------|
| **Web BFF** | 3 | 10 | CPU > 70% or RPS > 1000 |
| **Product Catalog** | 3 | 8 | CPU > 70% |
| **Cart Service** | 3 | 10 | CPU > 70% or RPS > 500 |
| **Order Service** | 2 | 5 | CPU > 70% |
| **Checkout Service** | 2 | 8 | Queue depth > 50 or CPU > 70% |

### Availability

- **Target SLA**: 99.9% uptime (43 minutes downtime/month)
- **Strategy**: Multiple replicas, rolling updates, health checks
- **Database**: SQL Server Always On (for production)

### Resilience

- **Circuit breaker**: Open after 5 consecutive failures
- **Timeout**: 5s default, 30s for checkout
- **Retry**: 3 attempts with exponential backoff (100ms, 500ms, 2s)
- **Bulkhead**: Isolate thread pools per external dependency

---

## Assumptions and Constraints

### Assumptions

1. **Kubernetes available**: Target environment supports Kubernetes (AKS, EKS, on-premises)
2. **SQL Server retained**: No database engine migration (PostgreSQL, MongoDB, etc.)
3. **Current tech stack**: Team familiar with .NET, C#, SQL Server
4. **No data loss**: Migration must preserve all existing data
5. **Zero downtime**: Strangler Fig pattern allows gradual migration

### Constraints

1. **Budget**: Use managed services where possible (AKS, Azure SQL, Service Bus)
2. **Timeline**: Incremental slices, each 2-4 weeks
3. **Team size**: 3-5 developers (pair on risky components)
4. **Testing**: Automated testing required before each slice deployment
5. **Rollback**: Must be able to revert each slice within 1 hour

### Out of Scope (for Initial Migration)

- ❌ Event Sourcing (future optimization)
- ❌ CQRS (future optimization)
- ❌ Service Mesh (Istio/Linkerd) - evaluate after migration
- ❌ GraphQL API - REST sufficient initially
- ❌ Real-time WebSockets (order status updates) - polling acceptable
- ❌ Multi-region deployment - single region initially
- ❌ Mobile apps - Web BFF supports desktop/mobile browsers

---

## Success Criteria

The target architecture is successfully achieved when:

✅ **All services independently deployable**: Can deploy Cart Service without touching Checkout
✅ **Each service owns its data**: Database split complete (Phase 2)
✅ **Event-driven communication**: OrderCreated, PaymentProcessed events published
✅ **Observability**: Distributed tracing shows end-to-end request flow
✅ **Zero downtime migration**: No user-visible outages during transition
✅ **Rollback capability**: Can revert each service independently
✅ **Performance maintained or improved**: p95 latencies ≤ monolith baseline
✅ **Behavioral equivalence**: All existing features work identically

---

## Next Steps

1. **Review and approve this Target Architecture** (Stakeholder sign-off)
2. **Read Migration Plan** (Migration-Plan.md) for step-by-step phases
3. **Review ADRs** for key architectural decisions
4. **Approve Slice 0 and Slice 1** before implementation begins

**Key Document**: See `/docs/Migration-Plan.md` for detailed migration phases and timelines.
