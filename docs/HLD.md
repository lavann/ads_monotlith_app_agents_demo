# High-Level Design (HLD)

## System Overview

**RetailMonolith** is an ASP.NET Core 8 monolithic web application that provides a complete e-commerce retail experience. The application implements a traditional three-tier architecture with a web presentation layer (Razor Pages), business logic layer (Services), and data access layer (Entity Framework Core).

### Purpose
This application serves as a demonstration platform for a retail system before microservices decomposition. It showcases a complete shopping experience including product browsing, cart management, checkout processing, and order tracking.

## Architecture Style

**Monolithic Architecture**: All functionality is deployed as a single application unit. The system follows a layered architecture pattern:

```
┌─────────────────────────────────────────────────────┐
│          Presentation Layer (Razor Pages)           │
│  /Products  /Cart  /Checkout  /Orders               │
└─────────────────────────────────────────────────────┘
                         │
                         ▼
┌─────────────────────────────────────────────────────┐
│           Business Logic Layer (Services)            │
│  CartService  CheckoutService  PaymentGateway        │
└─────────────────────────────────────────────────────┘
                         │
                         ▼
┌─────────────────────────────────────────────────────┐
│        Data Access Layer (Entity Framework)          │
│              AppDbContext + Models                   │
└─────────────────────────────────────────────────────┘
                         │
                         ▼
┌─────────────────────────────────────────────────────┐
│              SQL Server LocalDB                      │
└─────────────────────────────────────────────────────┘
```

## Core Components

### 1. Domain Boundaries

The application is organized around four primary domain areas:

#### Products Domain
- **Responsibility**: Product catalog management and display
- **Key Entities**: `Product`, `InventoryItem`
- **Endpoints**: `/Products` (Razor Page)
- **Features**: Product listing with category filtering, stock management

#### Cart Domain
- **Responsibility**: Shopping cart lifecycle management
- **Key Entities**: `Cart`, `CartLine`
- **Service**: `CartService` (ICartService)
- **Endpoints**: `/Cart` (Razor Page)
- **Features**: Add items, view cart contents, calculate totals

#### Checkout Domain
- **Responsibility**: Order processing and payment handling
- **Key Entities**: `Order`, `OrderLine`
- **Service**: `CheckoutService` (ICheckoutService)
- **Endpoints**: `/Checkout` (Razor Page), `POST /api/checkout` (Minimal API)
- **Features**: Payment processing, inventory reservation, order creation

#### Orders Domain
- **Responsibility**: Order history and tracking
- **Key Entities**: `Order`, `OrderLine`
- **Endpoints**: `/Orders` (Razor Page), `/Orders/Details` (Razor Page), `GET /api/orders/{id}` (Minimal API)
- **Features**: View order history, view order details

### 2. Cross-Cutting Components

#### Payment Gateway
- **Implementation**: `MockPaymentGateway` (IPaymentGateway)
- **Purpose**: Simulates external payment processing
- **Behavior**: Currently always returns successful payment results

#### Database Context
- **Implementation**: `AppDbContext` (DbContext)
- **Purpose**: Entity Framework Core context for data access
- **Features**: Automatic migrations, data seeding on startup

## Data Stores

### Primary Database: SQL Server LocalDB

**Connection String** (default):
```
Server=(localdb)\MSSQLLocalDB;Database=RetailMonolith;Trusted_Connection=True;MultipleActiveResultSets=true
```

**Tables/Entities**:
- `Products` - Product catalog
- `Inventory` - Stock levels (separate table, linked by SKU)
- `Carts` - Shopping carts (per customer)
- `CartLines` - Cart line items
- `Orders` - Completed orders
- `OrderLines` - Order line items

**Key Relationships**:
- Product ↔ InventoryItem: 1:1 relationship via SKU (unique index)
- Cart → CartLines: 1:N relationship
- Order → OrderLines: 1:N relationship
- All customer references use string-based "guest" identifier (no user table)

## External Dependencies

### Runtime Dependencies

1. **SQL Server LocalDB**
   - Purpose: Primary data store
   - Requirement: Included with Visual Studio or installable separately
   - Configuration: Connection string in appsettings.json or environment variables

2. **Entity Framework Core**
   - Version: 9.0.9
   - Purpose: ORM and database migrations
   - Packages: `Microsoft.EntityFrameworkCore.SqlServer`, `Microsoft.EntityFrameworkCore.Design`

3. **ASP.NET Core 8**
   - Target Framework: .NET 8.0
   - Purpose: Web application framework
   - Features: Razor Pages, Minimal APIs, Dependency Injection, Health Checks

4. **Microsoft.Extensions.Http.Polly**
   - Version: 9.0.9
   - Purpose: Resilience and transient fault handling (currently not actively used)

### Development Dependencies

- .NET 8 SDK
- SQL Server LocalDB or SQL Server instance
- Entity Framework Core CLI tools (for migrations)

## API Endpoints

### Razor Pages (Server-Side Rendered)

| Route | Method | Purpose |
|-------|--------|---------|
| `/` | GET | Home page |
| `/Products` | GET | List all active products |
| `/Products` | POST | Add product to cart (redirect to /Cart) |
| `/Cart` | GET | View shopping cart |
| `/Checkout` | GET | Display checkout form |
| `/Checkout` | POST | Process checkout and create order |
| `/Orders` | GET | List all orders (most recent first) |
| `/Orders/Details?id={id}` | GET | View specific order details |

### Minimal APIs (REST/JSON)

| Route | Method | Purpose | Request | Response |
|-------|--------|---------|---------|----------|
| `/api/checkout` | POST | Process checkout for guest user | None | `{ id, status, total }` |
| `/api/orders/{id}` | GET | Get order details | None | Full order object with lines |
| `/health` | GET | Health check endpoint | None | Health status |

## Runtime Assumptions

### Application Startup Sequence

1. **Configuration Loading**: Load appsettings.json and environment variables
2. **Service Registration**: Register DbContext, services, Razor Pages, health checks
3. **Database Migration**: Automatically run `db.Database.MigrateAsync()` on startup
4. **Data Seeding**: Run `AppDbContext.SeedAsync()` to populate 50 sample products (if database is empty)
5. **Middleware Pipeline**: Configure HTTPS redirection, static files, routing, authorization
6. **Endpoint Mapping**: Map Razor Pages and Minimal API endpoints
7. **Application Start**: Listen on `http://localhost:5000` and `https://localhost:5001`

### Deployment Model

- **Single Process**: All components run in one ASP.NET Core application process
- **Stateless**: No in-memory session state; all state persisted to database
- **Development Mode**: Uses LocalDB by default
- **Production Mode**: Requires connection string override via environment variable `ConnectionStrings__DefaultConnection`

### Scalability Considerations

- **Current State**: Single instance deployment
- **Database Connections**: Uses connection pooling via Entity Framework
- **Concurrency**: Optimistic concurrency (no explicit locking)
- **Session Management**: All users identified as "guest" (no authentication)

### Known Constraints

1. **No Authentication**: All operations use hardcoded "guest" customer ID
2. **No Authorization**: No role-based access control
3. **Single Currency**: Fixed to GBP (British Pounds)
4. **Mock Payment**: Payment gateway always succeeds (no real transactions)
5. **No Distributed Transactions**: Inventory and payment are not transactionally consistent
6. **No Event Sourcing**: State changes not captured as events (though comments suggest future event publishing)
7. **No Caching**: Every page load queries the database directly

## Configuration

### Environment Variables

| Variable | Description | Default |
|----------|-------------|---------|
| `ConnectionStrings__DefaultConnection` | Database connection string | LocalDB instance |
| `ASPNETCORE_ENVIRONMENT` | Environment name | Development |
| `ASPNETCORE_URLS` | URLs to listen on | `http://localhost:5000;https://localhost:5001` |

### Application Settings

Configuration is managed through:
- `appsettings.json` - Base configuration (logging only)
- `appsettings.Development.json` - Development overrides
- Environment variables - Runtime overrides

## Monitoring and Observability

### Health Checks

- **Endpoint**: `/health`
- **Purpose**: Basic application health status
- **Current Implementation**: Default ASP.NET Core health check (checks if application is running)

### Logging

- **Provider**: Microsoft.Extensions.Logging (Console)
- **Levels**: Information for application, Warning for ASP.NET Core
- **Configuration**: Set in appsettings.json

### Error Handling

- **Development**: Detailed error pages via developer exception page
- **Production**: Custom error handler at `/Error` endpoint
- **HSTS**: Enabled in non-development environments (30-day default)

## Future Decomposition Readiness

The codebase includes comments indicating preparation for microservices decomposition:

1. **Service Interfaces**: Business logic abstracted behind interfaces (ICartService, ICheckoutService, IPaymentGateway)
2. **API Endpoints**: Minimal APIs already exposed for checkout and orders
3. **Event Publishing**: Comments in CheckoutService suggest future event publishing (OrderCreated, PaymentProcessed, InventoryReserved)
4. **Domain Boundaries**: Clear separation between Products, Cart, Checkout, and Orders domains

## Security Considerations

### Current Implementation

- **HTTPS**: Enforced via HTTPS redirection middleware
- **HSTS**: Enabled in production
- **SQL Injection**: Protected via Entity Framework parameterized queries
- **CSRF**: Protected via ASP.NET Core anti-forgery tokens on POST forms

### Gaps (Not Implemented)

- No authentication or authorization
- No input validation on API endpoints
- No rate limiting
- No API authentication tokens
- Payment tokens accepted without validation
- No audit logging of sensitive operations
