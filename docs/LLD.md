# Low-Level Design (LLD)

## Module Overview

The RetailMonolith application is organized into the following modules:

```
RetailMonolith/
├── Data/              # Database context and configuration
├── Models/            # Entity/domain models
├── Services/          # Business logic layer
├── Pages/             # Razor Pages (UI + PageModels)
├── Migrations/        # EF Core database migrations
└── Program.cs         # Application entry point and configuration
```

---

## Data Layer

### AppDbContext

**File**: `Data/AppDbContext.cs`

**Purpose**: Entity Framework Core database context managing all entity sets and database operations.

**DbSets**:
```csharp
DbSet<Product> Products
DbSet<InventoryItem> Inventory
DbSet<Cart> Carts
DbSet<CartLine> CartLines
DbSet<Order> Orders
DbSet<OrderLine> OrderLines
```

**Key Methods**:

- `OnModelCreating(ModelBuilder)`: Configures entity relationships and constraints
  - Sets unique index on `Product.Sku`
  - Sets unique index on `InventoryItem.Sku`

- `SeedAsync(AppDbContext)`: Static method for database seeding
  - Checks if Products table is empty
  - Generates 50 sample products across 6 categories
  - Creates corresponding inventory items with random quantities (10-200)
  - Uses categories: Apparel, Footwear, Accessories, Electronics, Home, Beauty
  - Currency: GBP, Prices: £5-£105

**Dependencies**: Microsoft.EntityFrameworkCore, RetailMonolith.Models

**Usage Pattern**: 
- Injected as scoped dependency
- Auto-migrated on application startup
- Seeded once on first run

### DesignTimeDbContextFactory

**File**: `Data/DesignTimeDbContextFactory.cs`

**Purpose**: Factory for EF Core design-time operations (migrations, scaffolding)

**Implementation**: Implements `IDesignTimeDbContextFactory<AppDbContext>`

**Configuration**: 
- Hardcoded connection string: `Server=(localdb)\\MSSQLLocalDB;Database=RetailMonolith;...`
- Used only during `dotnet ef` commands

---

## Domain Models

### Product

**File**: `Models/Product.cs`

**Purpose**: Represents a product in the catalog

**Properties**:
```csharp
int Id              // Primary key
string Sku          // Unique identifier (indexed)
string Name         // Product name
string? Description // Optional description
decimal Price       // Price value
string Currency     // Currency code (e.g., "GBP")
bool IsActive       // Availability flag
string? Category    // Optional category
```

**Relationships**: 1:1 with InventoryItem (via Sku, not enforced by FK)

### InventoryItem

**File**: `Models/InventoryItem.cs`

**Purpose**: Tracks stock levels for products

**Properties**:
```csharp
int Id          // Primary key
string Sku      // Links to Product (unique index)
int Quantity    // Stock level
```

**Business Rules**: 
- Quantity decremented during checkout
- No negative quantity checks (potential bug)

### Cart and CartLine

**File**: `Models/Cart.cs`

**Purpose**: Shopping cart entities

**Cart Properties**:
```csharp
int Id                    // Primary key
string CustomerId         // Default: "guest"
List<CartLine> Lines      // Navigation property
```

**CartLine Properties**:
```csharp
int Id              // Primary key
int CartId          // Foreign key to Cart
Cart? Cart          // Navigation property
string Sku          // Product identifier
string Name         // Cached product name
decimal UnitPrice   // Cached price at add time
int Quantity        // Item count
```

**Design Notes**:
- Carts are per-customer (currently all "guest")
- Prices cached to preserve historical values
- No explicit total calculation (computed in memory)

### Order and OrderLine

**File**: `Models/Order.cs`

**Purpose**: Completed order entities

**Order Properties**:
```csharp
int Id                      // Primary key
DateTime CreatedUtc         // Default: DateTime.UtcNow
string CustomerId           // Default: "guest"
string Status               // "Created"|"Paid"|"Failed"|"Shipped"
decimal Total               // Order total (cached)
List<OrderLine> Lines       // Navigation property
```

**OrderLine Properties**:
```csharp
int Id              // Primary key
int OrderId         // Foreign key to Order
Order? Order        // Navigation property
string Sku          // Product identifier
string Name         // Cached product name
decimal UnitPrice   // Cached price at checkout
int Quantity        // Item count
```

**Status Flow**: Created → Paid (on successful payment) or Failed (on payment failure)

---

## Service Layer

### ICartService / CartService

**Files**: `Services/ICartService.cs`, `Services/CartService.cs`

**Purpose**: Encapsulates shopping cart business logic

**Dependencies**: `AppDbContext`

**Public Methods**:

1. `GetOrCreateCartAsync(string customerId, CancellationToken)`
   - Retrieves existing cart or creates new one
   - Includes cart lines via eager loading
   - Saves new cart immediately if created

2. `AddToCartAsync(string customerId, int productId, int quantity, CancellationToken)`
   - Validates product exists
   - Gets or creates cart
   - If product already in cart: increments quantity
   - If new product: adds new CartLine
   - Saves changes to database

3. `GetCartWithLinesAsync(string customerId, CancellationToken)`
   - Retrieves cart with lines
   - Returns empty cart instance if not found (doesn't persist)

4. `ClearCartAsync(string customerId, CancellationToken)`
   - Finds cart with lines
   - Removes entire cart entity (cascades to lines)
   - Used after successful checkout

**Implementation Details**:
- Uses `Include(c => c.Lines)` for eager loading
- Direct database queries (no caching)
- Synchronous save after each operation

### ICheckoutService / CheckoutService

**Files**: `Services/ICheckoutService.cs`, `Services/CheckoutService .cs`

**Purpose**: Orchestrates checkout process

**Dependencies**: `AppDbContext`, `IPaymentGateway`

**Public Method**:

`CheckoutAsync(string customerId, string paymentToken, CancellationToken)`

**Checkout Flow**:

1. **Pull Cart**: 
   - Query cart with lines via `Include()`
   - Throw if cart not found

2. **Calculate Total**: 
   - Sum: `cart.Lines.Sum(l => l.UnitPrice * l.Quantity)`

3. **Reserve Inventory**: 
   - For each cart line:
     - Query `Inventory` by SKU (uses `SingleAsync` - throws if not found)
     - Check `inv.Quantity >= line.Quantity`, throw if insufficient
     - Decrement: `inv.Quantity -= line.Quantity`

4. **Process Payment**: 
   - Call `_payments.ChargeAsync(new PaymentRequest(total, "GBP", paymentToken))`
   - Set order status: "Paid" if succeeded, "Failed" otherwise

5. **Create Order**: 
   - New `Order` with status, total, customer ID
   - Map cart lines to order lines
   - Add to `Orders` DbSet

6. **Clear Cart**: 
   - Remove all `CartLines` (not the Cart entity itself)
   - Save all changes in single transaction

7. **Return Order**: 
   - Return created order object

**Potential Issues**:
- No compensation if payment succeeds but order creation fails
- Inventory reserved before payment (could oversell if payment fails)
- Comment indicates future event publishing: `// (future) publish events here`

### IPaymentGateway / MockPaymentGateway

**Files**: `Services/IPaymentGateway.cs`, `Services/MockPaymentGateway.cs`

**Purpose**: Abstraction for payment processing

**Records**:
```csharp
record PaymentRequest(decimal Amount, string Currency, string Token)
record PaymentResult(bool Succeeded, string? ProviderRef, string? Error)
```

**Method**: `ChargeAsync(PaymentRequest, CancellationToken)`

**Mock Implementation**:
- Always returns success: `Succeeded = true`
- Generates mock reference: `MOCK-{Guid.NewGuid():N}`
- No actual payment processing
- Comment: `// trivial success for hack; add a random fail to demo error path if you like`

---

## Presentation Layer (Razor Pages)

### Products/Index

**File**: `Pages/Products/Index.cshtml.cs`

**Purpose**: Product catalog page

**Dependencies**: `AppDbContext`, `ICartService`

**Page Properties**:
```csharp
IList<Product> Products              // Bound product list
Dictionary<string, string> CategoryImages  // Category to image URL mapping
```

**Handlers**:

1. `OnGetAsync()`: 
   - Query: `_db.Products.Where(p => p.IsActive).ToListAsync()`
   - Populates Products list

2. `OnPostAsync(int productId)`:
   - Finds product by ID
   - Gets or creates cart for "guest"
   - Adds new CartLine with quantity = 1
   - Calls `_cartService.AddToCartAsync("guest", productId)`
   - Redirects to `/Cart`

**Image Mapping**: Static dictionary maps categories to Unsplash image URLs

**Issues**:
- Duplicate cart logic (both inline and via service call)
- Commented-out code left in place (lines 58-61)
- Direct database access mixed with service usage

### Cart/Index

**File**: `Pages/Cart/Index.cshtml.cs`

**Purpose**: Shopping cart display

**Dependencies**: `ICartService`

**Page Properties**:
```csharp
List<(string Name, int Quantity, decimal Price)> Lines  // Denormalized cart items
decimal Total { get; }                                   // Computed property
```

**Handler**:

`OnGetAsync()`:
- Calls `_cartService.GetCartWithLinesAsync("guest")`
- Projects cart lines to tuples for display
- Total computed as: `Lines.Sum(line => line.Price * line.Quantity)`

**Design**: View-optimized projection (tuple instead of full entity)

### Checkout/Index

**File**: `Pages/Checkout/Index.cshtml.cs`

**Purpose**: Checkout form and processing

**Dependencies**: `ICartService`, `ICheckoutService`

**Page Properties**:
```csharp
List<(string Name, int Qty, decimal Price)> Lines  // Cart summary
decimal Total { get; }                             // Computed total
[BindProperty] string PaymentToken                 // Default: "tok_test"
```

**Handlers**:

1. `OnGetAsync()`:
   - Retrieves cart via `_cartService.GetCartWithLinesAsync("guest")`
   - Projects to tuple list for display

2. `OnPostAsync()`:
   - Validates model state
   - Calls `_checkoutService.CheckoutAsync("guest", PaymentToken)`
   - Redirects to `/Orders/Details?id={order.Id}` on success
   - Returns to page on validation failure

**Default Behavior**: Uses hardcoded "tok_test" payment token

### Orders/Index

**File**: `Pages/Orders/Index.cshtml.cs`

**Purpose**: Order history list

**Dependencies**: `AppDbContext`

**Page Properties**:
```csharp
List<Order> Orders  // All orders with lines
```

**Handler**:

`OnGetAsync()`:
- Query: `_db.Orders.Include(o => o.Lines).OrderByDescending(o => o.CreatedUtc).ToListAsync()`
- Loads all orders (no customer filtering)
- Most recent first

**Issue**: No pagination, shows all orders for all customers

### Orders/Details

**File**: `Pages/Orders/Details.cshtml.cs`

**Purpose**: Single order details

**Dependencies**: `AppDbContext`

**Page Properties**:
```csharp
Order? Order  // Nullable order
```

**Handler**:

`OnGetAsync(int id)`:
- Query: `_db.Orders.Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == id)`
- Returns null if not found (handled in view)

**Issue**: No authorization check (any user can view any order)

---

## Request Flow Examples

### Flow 1: Browse and Add to Cart

```
User → GET /Products
    → ProductsPageModel.OnGetAsync()
        → db.Products.Where(p => p.IsActive).ToListAsync()
    ← Render product list

User → POST /Products (productId=5)
    → ProductsPageModel.OnPostAsync(5)
        → db.Products.FindAsync(5)
        → cartService.AddToCartAsync("guest", 5)
            → db.Carts (get or create)
            → Check existing line or add new
            → db.SaveChangesAsync()
    ← Redirect to /Cart
```

### Flow 2: View Cart

```
User → GET /Cart
    → CartPageModel.OnGetAsync()
        → cartService.GetCartWithLinesAsync("guest")
            → db.Carts.Include(c => c.Lines).FirstOrDefaultAsync(...)
        → Project to tuple list
    ← Render cart with totals
```

### Flow 3: Checkout

```
User → GET /Checkout
    → CheckoutPageModel.OnGetAsync()
        → cartService.GetCartWithLinesAsync("guest")
    ← Render checkout form

User → POST /Checkout (PaymentToken="tok_test")
    → CheckoutPageModel.OnPostAsync()
        → checkoutService.CheckoutAsync("guest", "tok_test")
            1. Load cart + lines
            2. Calculate total
            3. Reserve inventory (foreach line)
            4. paymentGateway.ChargeAsync(...)
            5. Create order + lines
            6. Clear cart lines
            7. db.SaveChangesAsync()
        ← Return Order
    ← Redirect to /Orders/Details?id={orderId}
```

### Flow 4: Minimal API Checkout

```
Client → POST /api/checkout
    → Minimal API handler
        → checkoutService.CheckoutAsync("guest", "tok_test")
            [same flow as above]
        ← Return JSON: { id, status, total }
```

### Flow 5: View Orders

```
User → GET /Orders
    → OrdersPageModel.OnGetAsync()
        → db.Orders.Include(o => o.Lines).OrderByDescending(...).ToListAsync()
    ← Render order list

User → GET /Orders/Details?id=10
    → OrderDetailsPageModel.OnGetAsync(10)
        → db.Orders.Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == 10)
    ← Render order details
```

---

## Areas of Coupling

### High Coupling Areas

1. **Checkout Service ↔ Multiple Concerns**
   - **Tight coupling**: Directly manages inventory, payment, order creation, cart clearing
   - **Impact**: Changes to any domain require touching CheckoutService
   - **Risk**: Transaction boundaries unclear, no compensation logic

2. **Direct DbContext Injection in Page Models**
   - **Location**: Products/Index, Orders/Index, Orders/Details
   - **Issue**: Bypasses service layer, duplicates business logic
   - **Example**: Products/Index has cart logic both inline and via CartService

3. **Hardcoded "guest" Customer ID**
   - **Pervasive**: Used in every service and page model
   - **Impact**: No multi-user support, tight coupling to anonymous sessions
   - **Files**: All page models, all services

4. **Currency Hardcoded as "GBP"**
   - **Locations**: AppDbContext.SeedAsync(), CheckoutService
   - **Impact**: No internationalization support

### Database Coupling

1. **Schema Changes Impact Multiple Layers**
   - Entity models used directly in service layer and page models
   - No DTOs or view models (except tuple projections)
   - Migration requires code changes across layers

2. **SKU as Integration Point**
   - Product and InventoryItem linked by string SKU (not FK)
   - Cart and Order lines store SKU (not Product FK)
   - Fragile: SKU changes break referential integrity

### Temporal Coupling

1. **Checkout Process Ordering**
   - Inventory must be reserved before payment
   - Cart must exist before checkout
   - Order depends on successful payment (but inventory already changed)

2. **Startup Dependencies**
   - Database must migrate before seeding
   - Seeding must complete before app starts serving

---

## Hotspots (High Churn / Complex Areas)

### 1. CheckoutService.CheckoutAsync

**Complexity**: High
- Orchestrates 7 distinct operations
- Multiple database queries (cart, inventory per item, order creation)
- External call (payment gateway)
- Transaction boundary unclear (what happens on partial failure?)

**Future Changes**: 
- Comment indicates event publishing planned
- Compensation logic needed
- Distributed transaction coordination

### 2. CartService.AddToCartAsync

**Complexity**: Medium
- Conditional logic (existing vs new cart line)
- Multiple database operations
- Used by both page models and potentially APIs

**Issue**: No quantity validation (can add negative quantities)

### 3. Products/Index.OnPostAsync

**Complexity**: Low but problematic
- Duplicate cart management logic
- Commented-out code
- Mix of direct DB access and service calls

**Refactoring Need**: Should use CartService exclusively

### 4. AppDbContext.SeedAsync

**Complexity**: Medium
- Random data generation
- Batch operations (50 products + inventory items)
- Runs on every startup (checks if needed)

**Risk**: Large seeding operations could delay startup

### 5. Order Status Management

**Current State**: Status is a simple string property
- Values: "Created", "Paid", "Failed", "Shipped"
- No state machine or validation
- Can be set to arbitrary values

**Hotspot**: Likely to become complex as order lifecycle expands

---

## Technical Debt

### Code Quality Issues

1. **Commented Code**: Products/Index.OnPostAsync has commented-out logic
2. **Empty XML Docs**: DesignTimeDbContextFactory has empty summary tags
3. **Inconsistent Service Usage**: Some pages use services, others query DbContext directly
4. **No Validation**: Missing input validation on API endpoints, cart quantities, payment tokens
5. **Magic Strings**: "guest", "tok_test", "GBP", status values not as constants

### Design Gaps

1. **No Unit Tests**: No test files found in repository
2. **No DTOs**: Entities exposed directly to presentation layer (except tuple projections)
3. **No Logging**: No structured logging of business events (checkout, payment, inventory changes)
4. **No Error Handling**: Exceptions bubble up unhandled (except CheckoutAsync throws)
5. **No Pagination**: Orders/Index loads all orders into memory

### Security Concerns

1. **No Authentication**: All operations as "guest"
2. **No Authorization**: Anyone can view any order
3. **No API Security**: No rate limiting, no API keys
4. **No Input Sanitization**: Trust all user input
5. **Payment Token Validation**: Tokens accepted without verification

### Performance Issues

1. **N+1 Queries**: CheckoutService queries inventory in a loop
2. **No Caching**: Every page load hits database
3. **No Async Views**: Razor pages call async methods but views are synchronous
4. **Eager Loading Always**: All `Include()` operations load full navigation properties
5. **No Connection Pooling Config**: Using defaults

### Operational Gaps

1. **No Monitoring**: Health check endpoint basic, no metrics
2. **No Distributed Tracing**: Can't track request flows
3. **No Correlation IDs**: No request tracking across operations
4. **Error Recovery**: No retry logic for transient failures
5. **Database Backups**: No documented backup/restore procedures

---

## Extensibility Points

### Well-Designed for Extension

1. **Service Interfaces**: ICartService, ICheckoutService, IPaymentGateway easily mockable/replaceable
2. **Dependency Injection**: Scoped registration allows swapping implementations
3. **Minimal APIs**: Already exposed, ready for client consumption
4. **Domain Separation**: Clear boundaries make extraction possible

### Areas Resistant to Change

1. **Customer Identity**: "guest" hardcoded everywhere
2. **Currency**: GBP assumptions throughout
3. **Cart-to-Order Mapping**: Direct field copying in CheckoutService
4. **Status Strings**: Status values not centralized

---

## Recommendations for Improvement

### Short-Term (Low Risk)

1. Extract magic strings to constants class
2. Add structured logging to CheckoutService
3. Consolidate cart management in Products/Index to use CartService only
4. Add pagination to Orders/Index
5. Remove commented-out code

### Medium-Term (Moderate Risk)

1. Introduce DTOs/ViewModels to decouple entities from views
2. Add input validation using FluentValidation or Data Annotations
3. Implement proper error handling and user-friendly messages
4. Add unit and integration tests
5. Implement correlation ID tracking

### Long-Term (Higher Risk, Architectural)

1. Replace "guest" with proper session or authentication
2. Implement saga pattern for checkout (inventory reservation + payment + order creation)
3. Extract payment, inventory, and order domains into separate bounded contexts
4. Add event sourcing/publishing for domain events
5. Implement API gateway for minimal APIs with authentication
