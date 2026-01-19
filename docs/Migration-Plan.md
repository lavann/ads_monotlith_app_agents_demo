# Migration Plan: Monolith to Microservices

## Executive Summary

This document provides a detailed, phase-by-phase plan for migrating the RetailMonolith application from a monolithic architecture to containerized microservices using the **Strangler Fig pattern**. The migration is designed to be **incremental, low-risk, and reversible**, ensuring the application remains functional throughout the transition.

**Migration Strategy**: Strangler Fig Pattern
- Gradually replace monolith functionality with microservices
- Maintain both old and new systems during transition
- Route traffic to new services incrementally
- Preserve rollback capability at each phase
- No "big bang" cutover

**Timeline**: 8 slices over 6-8 months (24-32 weeks total, each slice = 2-5 weeks)

**First Slice**: Product Catalog Service (read-only extraction)

---

## Guiding Principles

1. **Incremental Delivery**: Each slice delivers working, deployable software
2. **Behavioral Preservation**: Existing functionality continues to work identically
3. **Rollback Safety**: Each phase has a documented rollback procedure
4. **Testing First**: Comprehensive tests before and after each slice
5. **Monitoring**: Observe metrics before changing architecture
6. **Team Velocity**: Balance speed with safety and learning
7. **Documentation**: Update docs with each slice

---

## Risk Management

### High-Level Risks

| Risk | Impact | Mitigation | Trigger for Rollback |
|------|--------|------------|---------------------|
| **Performance degradation** | User experience suffers | Baseline metrics, load testing before cutover | p95 latency > 2x baseline |
| **Data inconsistency** | Orders incorrect, inventory oversold | Shared database initially, transaction boundaries | Data validation failures |
| **Service unavailability** | Users cannot complete checkout | Health checks, circuit breakers, multiple replicas | Error rate > 5% |
| **Configuration errors** | Services can't communicate | Config validation, dry-run deployments | Service discovery failures |
| **Breaking changes** | API contract violations | API versioning, contract tests | Integration test failures |

### General Rollback Strategy

**When to Roll Back:**
- Error rate increases by more than 5%
- p95 latency increases by more than 100%
- Critical functionality broken (cannot checkout, cannot view orders)
- Security vulnerability introduced

**How to Roll Back:**
1. **Immediate**: Route traffic back to monolith (Kubernetes Ingress change)
2. **Short-term**: Redeploy previous container version (`kubectl rollout undo`)
3. **Data**: Shared database prevents data loss during rollback
4. **Events**: Stop consuming new events, replay if needed

**Rollback SLA**: Within 15 minutes of decision to roll back

---

## Slice 0: Preparation and Foundation

### Objective

Establish infrastructure, observability, and containerization foundation **without changing application behavior**.

### Duration
**2-3 weeks**

### Tasks

#### 1. Containerize Existing Monolith

**Deliverable**: Dockerfile and Docker Compose for local development

**Steps:**
```dockerfile
# Create /Dockerfile in repository root
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["RetailMonolith.csproj", "./"]
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "RetailMonolith.dll"]
```

**docker-compose.yml** (local development):
```yaml
version: '3.8'
services:
  web:
    build: .
    ports:
      - "8080:8080"
    environment:
      - ASPNETCORE_ENVIRONMENT=Development
      - ConnectionStrings__DefaultConnection=Server=sqlserver;Database=RetailMonolith;User Id=sa;Password=YourStrong@Passw0rd;TrustServerCertificate=True
    depends_on:
      - sqlserver

  sqlserver:
    image: mcr.microsoft.com/mssql/server:2022-latest
    environment:
      - ACCEPT_EULA=Y
      - SA_PASSWORD=YourStrong@Passw0rd
    ports:
      - "1433:1433"
    volumes:
      - sqldata:/var/opt/mssql

volumes:
  sqldata:
```

**Acceptance Criteria:**
- ✅ `docker build` succeeds
- ✅ `docker-compose up` starts application and database
- ✅ Application accessible at http://localhost:8080
- ✅ All existing functionality works (products, cart, checkout, orders)
- ✅ Database migrations run automatically on startup

---

#### 2. Establish Baseline Metrics

**Deliverable**: Performance baseline document and monitoring dashboard

**Metrics to Capture:**
```
┌────────────────────────────────────────────────────────┐
│                   Baseline Metrics                      │
├────────────────────────────────────────────────────────┤
│ Endpoint              │ p50  │ p95  │ p99  │ RPS       │
├───────────────────────┼──────┼──────┼──────┼───────────┤
│ GET /Products         │ 45ms │ 120ms│ 250ms│ 50 req/s  │
│ POST /Products (Add)  │ 30ms │ 80ms │ 150ms│ 10 req/s  │
│ GET /Cart             │ 25ms │ 60ms │ 100ms│ 30 req/s  │
│ POST /Checkout        │ 150ms│ 400ms│ 800ms│ 5 req/s   │
│ GET /Orders           │ 50ms │ 130ms│ 280ms│ 15 req/s  │
│ GET /Orders/Details   │ 30ms │ 75ms │ 120ms│ 10 req/s  │
├───────────────────────┴──────┴──────┴──────┴───────────┤
│ Database              │ Avg  │ Max  │ Pool │ QPS       │
├───────────────────────┼──────┼──────┼──────┼───────────┤
│ Query Duration        │ 8ms  │ 50ms │ 10   │ 120 q/s   │
│ Connection Pool Usage │ 3    │ 7    │ 100  │ -         │
└────────────────────────────────────────────────────────┘
```

**Tools:**
- **Application Insights** (if Azure) or **Prometheus** (on-premises)
- **K6** or **Apache JMeter** for load testing
- **SQL Server Profiler** for database metrics

**Steps:**
1. Instrument existing monolith with OpenTelemetry or Application Insights SDK
2. Run load tests simulating realistic traffic (100 concurrent users, 10-minute duration)
3. Capture metrics in spreadsheet or document
4. Establish alert thresholds (error rate > 1%, p95 > 500ms)

**Acceptance Criteria:**
- ✅ Baseline metrics document created (`/docs/Baseline-Metrics.md`)
- ✅ Load tests pass with <1% error rate
- ✅ Metrics dashboard created (Grafana or Application Insights)

---

#### 3. Set Up Kubernetes Cluster

**Deliverable**: Working Kubernetes cluster with ingress controller

**Options:**
- **Azure Kubernetes Service (AKS)**: `az aks create --name retail-cluster --resource-group retail-rg`
- **Local (Development)**: Minikube, Docker Desktop, or Kind

**Components:**
```bash
# Install NGINX Ingress Controller
kubectl apply -f https://raw.githubusercontent.com/kubernetes/ingress-nginx/controller-v1.9.5/deploy/static/provider/cloud/deploy.yaml

# Install Prometheus & Grafana (monitoring)
helm repo add prometheus-community https://prometheus-community.github.io/helm-charts
helm install prometheus prometheus-community/kube-prometheus-stack

# Verify
kubectl get pods -n ingress-nginx
kubectl get pods -n monitoring
```

**Acceptance Criteria:**
- ✅ Kubernetes cluster accessible (`kubectl get nodes` succeeds)
- ✅ Ingress controller running
- ✅ Prometheus and Grafana accessible
- ✅ Can deploy sample workload (nginx pod)

---

#### 4. Deploy Monolith to Kubernetes

**Deliverable**: Monolith running in Kubernetes (containerized monolith)

**Kubernetes Manifests** (`/k8s/monolith/`):

**deployment.yaml:**
```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: retailmonolith
  labels:
    app: retailmonolith
spec:
  replicas: 3
  selector:
    matchLabels:
      app: retailmonolith
  template:
    metadata:
      labels:
        app: retailmonolith
    spec:
      containers:
      - name: web
        image: retailmonolith:latest
        ports:
        - containerPort: 8080
        env:
        - name: ASPNETCORE_ENVIRONMENT
          value: "Production"
        - name: ConnectionStrings__DefaultConnection
          valueFrom:
            secretKeyRef:
              name: database-secret
              key: connection-string
        livenessProbe:
          httpGet:
            path: /health
            port: 8080
          initialDelaySeconds: 30
          periodSeconds: 10
        readinessProbe:
          httpGet:
            path: /health
            port: 8080
          initialDelaySeconds: 10
          periodSeconds: 5
        resources:
          requests:
            memory: "256Mi"
            cpu: "200m"
          limits:
            memory: "512Mi"
            cpu: "1000m"
```

**service.yaml:**
```yaml
apiVersion: v1
kind: Service
metadata:
  name: retailmonolith-service
spec:
  selector:
    app: retailmonolith
  ports:
  - port: 80
    targetPort: 8080
  type: ClusterIP
```

**ingress.yaml:**
```yaml
apiVersion: networking.k8s.io/v1
kind: Ingress
metadata:
  name: retailmonolith-ingress
  annotations:
    nginx.ingress.kubernetes.io/rewrite-target: /
spec:
  rules:
  - host: retail.local
    http:
      paths:
      - path: /
        pathType: Prefix
        backend:
          service:
            name: retailmonolith-service
            port:
              number: 80
```

**Deploy:**
```bash
kubectl apply -f k8s/monolith/
kubectl get pods -l app=retailmonolith
kubectl logs -l app=retailmonolith
```

**Acceptance Criteria:**
- ✅ 3 replicas running and healthy
- ✅ Application accessible via Ingress URL
- ✅ Health checks passing
- ✅ Database migrations run on startup
- ✅ All functionality works (smoke test: add to cart, checkout, view orders)
- ✅ Metrics visible in Prometheus/Grafana

---

#### 5. Add Health Checks and Observability

**Deliverable**: Enhanced health checks and distributed tracing

**Code Changes** (in `Program.cs`):

```csharp
// Add health checks
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>("database")
    .AddUrlGroup(new Uri("https://api.stripe.com"), "payment-gateway", tags: new[] { "external" });

// Add OpenTelemetry
builder.Services.AddOpenTelemetry()
    .WithTracing(tracerProviderBuilder =>
    {
        tracerProviderBuilder
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddSqlClientInstrumentation()
            .AddJaegerExporter(options =>
            {
                options.AgentHost = Environment.GetEnvironmentVariable("JAEGER_AGENT_HOST") ?? "localhost";
                options.AgentPort = int.Parse(Environment.GetEnvironmentVariable("JAEGER_AGENT_PORT") ?? "6831");
            });
    });

// Health check endpoints
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false // No checks, just "alive"
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready") || check.Name == "database"
});
```

**Acceptance Criteria:**
- ✅ `/health/live` returns 200 (liveness)
- ✅ `/health/ready` returns 200 when database connected
- ✅ Traces visible in Jaeger UI
- ✅ Requests show correlation IDs in logs

---

#### 6. Create API Contract Tests

**Deliverable**: Integration test suite for existing APIs

**Test Framework**: xUnit + WebApplicationFactory

**Sample Test** (`/Tests/Integration/CheckoutApiTests.cs`):
```csharp
public class CheckoutApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public CheckoutApiTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task PostCheckout_WithValidCart_ReturnsOrder()
    {
        // Arrange: Add item to cart
        await _client.PostAsync("/api/cart/guest/items", 
            JsonContent.Create(new { sku = "WIDGET-001", quantity = 2 }));

        // Act: Checkout
        var response = await _client.PostAsync("/api/checkout", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var order = await response.Content.ReadFromJsonAsync<OrderResponse>();
        order.Status.Should().Be("Paid");
        order.Total.Should().BeGreaterThan(0);
    }
}
```

**Acceptance Criteria:**
- ✅ Test suite covers all critical paths (add to cart, checkout, view orders)
- ✅ All tests pass against containerized monolith
- ✅ Tests run in CI/CD pipeline

---

### Slice 0 Exit Criteria

**Before proceeding to Slice 1, verify:**

- ✅ Monolith runs in Docker locally and in Kubernetes
- ✅ Baseline metrics captured and documented
- ✅ Health checks implemented and functional
- ✅ Distributed tracing configured (Jaeger or Application Insights)
- ✅ Integration test suite passes (100% pass rate)
- ✅ Monitoring dashboards created (Grafana or Application Insights)
- ✅ Team trained on Kubernetes basics (kubectl, logs, describe)
- ✅ Rollback procedure tested (redeploy previous version)

**Artifacts Delivered:**
- `/Dockerfile`
- `/docker-compose.yml`
- `/k8s/monolith/` (Kubernetes manifests)
- `/docs/Baseline-Metrics.md`
- `/Tests/Integration/` (contract tests)

---

## Slice 1: Extract Product Catalog Service (Read-Only)

### Objective

Extract the Product Catalog domain as a separate read-only service, routing product list requests to the new service while keeping all other functionality in the monolith.

### Why This Slice First?

✅ **Low Risk**: Read-only operations, no writes to break
✅ **High Value**: Products domain is well-isolated
✅ **Clear Boundary**: Products and Inventory tables have minimal dependencies
✅ **Demonstrable**: Easy to show side-by-side comparison (monolith vs. service)
✅ **No Data Migration**: Service reads from shared database initially
✅ **Rollback Simple**: Route traffic back to monolith

### Duration
**3-4 weeks**

---

### Phase 1.1: Create Product Catalog Service

#### 1. Create New .NET Project

**Directory Structure:**
```
/services/
  /ProductCatalogService/
    ProductCatalogService.csproj
    Program.cs
    /Controllers/
      ProductsController.cs
    /Models/
      Product.cs
      InventoryItem.cs
    /Data/
      AppDbContext.cs  (subset: Products, Inventory only)
    Dockerfile
```

**ProductsController.cs**:
```csharp
[ApiController]
[Route("api/[controller]")]
public class ProductsController : ControllerBase
{
    private readonly AppDbContext _db;

    public ProductsController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> GetProducts([FromQuery] string? category)
    {
        var query = _db.Products.Where(p => p.IsActive);
        
        if (!string.IsNullOrEmpty(category))
            query = query.Where(p => p.Category == category);

        var products = await query.ToListAsync();
        return Ok(products);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetProduct(int id)
    {
        var product = await _db.Products
            .FirstOrDefaultAsync(p => p.Id == id && p.IsActive);
        
        return product == null ? NotFound() : Ok(product);
    }

    [HttpGet("{id}/inventory")]
    public async Task<IActionResult> GetInventory(int id)
    {
        var product = await _db.Products.FindAsync(id);
        if (product == null) return NotFound();

        var inventory = await _db.Inventory
            .FirstOrDefaultAsync(i => i.Sku == product.Sku);
        
        return inventory == null 
            ? Ok(new { sku = product.Sku, quantity = 0 })
            : Ok(new { sku = inventory.Sku, quantity = inventory.Quantity });
    }
}
```

**Program.cs**:
```csharp
var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddControllers();
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Health checks
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>("database");

// OpenTelemetry (tracing)
builder.Services.AddOpenTelemetry()
    .WithTracing(tracerProviderBuilder =>
        tracerProviderBuilder
            .AddAspNetCoreInstrumentation()
            .AddSqlClientInstrumentation());

var app = builder.Build();

app.MapControllers();
app.MapHealthChecks("/health/live");
app.MapHealthChecks("/health/ready");

app.Run();
```

**Dockerfile**:
```dockerfile
# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
# Copy project file(s) - adjust path based on your project structure
COPY ["*.csproj", "./"]
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app/publish

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
EXPOSE 8081
COPY --from=build /app/publish .
# IMPORTANT: Replace with your actual service DLL name
ENTRYPOINT ["dotnet", "YourServiceName.dll"]
```
**Note**: 
- Adjust COPY paths based on your specific project structure
- **Replace `YourServiceName.dll`** with actual service name (e.g., `ProductCatalogService.dll`)

**Acceptance Criteria:**
- ✅ Service builds successfully (`dotnet build`)
- ✅ Runs locally (`dotnet run`)
- ✅ `/api/products` returns product list
- ✅ `/api/products/{id}` returns single product
- ✅ `/api/products/{id}/inventory` returns stock level

---

#### 2. Deploy Product Catalog Service to Kubernetes

**Kubernetes Manifests** (`/k8s/product-catalog/`):

**deployment.yaml**:
```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: product-catalog-service
spec:
  replicas: 3
  selector:
    matchLabels:
      app: product-catalog
  template:
    metadata:
      labels:
        app: product-catalog
    spec:
      containers:
      - name: api
        image: product-catalog-service:v1.0.0
        ports:
        - containerPort: 8081
        env:
        - name: ConnectionStrings__DefaultConnection
          valueFrom:
            secretKeyRef:
              name: database-secret
              key: connection-string
        livenessProbe:
          httpGet:
            path: /health/live
            port: 8081
        readinessProbe:
          httpGet:
            path: /health/ready
            port: 8081
        resources:
          requests:
            memory: "128Mi"
            cpu: "100m"
          limits:
            memory: "256Mi"
            cpu: "500m"
```

**service.yaml**:
```yaml
apiVersion: v1
kind: Service
metadata:
  name: product-catalog-service
spec:
  selector:
    app: product-catalog
  ports:
  - port: 8081
    targetPort: 8081
  type: ClusterIP
```

**Deploy:**
```bash
kubectl apply -f k8s/product-catalog/
kubectl get pods -l app=product-catalog
kubectl logs -l app=product-catalog
```

**Acceptance Criteria:**
- ✅ 3 replicas running
- ✅ Health checks passing
- ✅ Service accessible from within cluster: `curl http://product-catalog-service:8081/api/products`

---

### Phase 1.2: Route Traffic to New Service

#### 1. Update Web BFF to Call Product Catalog Service

**Modify `/Pages/Products/Index.cshtml.cs`:**

**Before (direct database access):**
```csharp
public async Task OnGetAsync()
{
    Products = await _db.Products.Where(p => p.IsActive).ToListAsync();
}
```

**After (calls Product Catalog Service):**
```csharp
private readonly IHttpClientFactory _httpClientFactory;
private readonly IConfiguration _config;
private readonly AppDbContext _db; // Fallback

public IndexModel(IHttpClientFactory httpClientFactory, IConfiguration config, AppDbContext db)
{
    _httpClientFactory = httpClientFactory;
    _config = config;
    _db = db;
}

public async Task OnGetAsync()
{
    try
    {
        var httpClient = _httpClientFactory.CreateClient("ProductCatalog");
        var response = await httpClient.GetAsync("/api/products");
        
        if (response.IsSuccessStatusCode)
        {
            Products = await response.Content.ReadFromJsonAsync<List<Product>>() ?? new();
        }
        else
        {
            // Fallback to direct database (safety net during migration)
            Products = await _db.Products.Where(p => p.IsActive).ToListAsync();
        }
    }
    catch (Exception ex)
    {
        // Log error and fall back to database
        _logger.LogWarning(ex, "Failed to call Product Catalog Service, falling back to database");
        Products = await _db.Products.Where(p => p.IsActive).ToListAsync();
    }
}
```

**Configure IHttpClientFactory in `Program.cs` (Web BFF):**
```csharp
builder.Services.AddHttpClient("ProductCatalog", client =>
{
    var url = builder.Configuration["ProductCatalogServiceUrl"] ?? "http://product-catalog-service:8081";
    client.BaseAddress = new Uri(url);
    client.Timeout = TimeSpan.FromSeconds(5);
})
.AddTransientHttpErrorPolicy(policy => 
    policy.WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromMilliseconds(100 * Math.Pow(2, retryAttempt))))
.AddTransientHttpErrorPolicy(policy => 
    policy.CircuitBreakerAsync(5, TimeSpan.FromSeconds(30)));
```

**Note**: Using `IHttpClientFactory` prevents socket exhaustion issues that can occur with direct `HttpClient` instantiation.

**Configuration (`appsettings.json` in Web BFF):**
```json
{
  "ProductCatalogServiceUrl": "http://product-catalog-service:8081"
}
```

**Acceptance Criteria:**
- ✅ Products page loads successfully
- ✅ Products list matches previous behavior
- ✅ Traces show request: Web BFF → Product Catalog Service
- ✅ If Product Catalog Service down, falls back to database

---

#### 2. Add Resilience Policies

**Install Polly** (if not already):
```bash
dotnet add package Microsoft.Extensions.Http.Polly
```

**Configure in `Program.cs` (Web BFF):**
```csharp
builder.Services.AddHttpClient("ProductCatalog", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["ProductCatalogServiceUrl"]);
    client.Timeout = TimeSpan.FromSeconds(5);
})
.AddTransientHttpErrorPolicy(policyBuilder =>
    policyBuilder.WaitAndRetryAsync(3, retryAttempt => 
        TimeSpan.FromMilliseconds(100 * Math.Pow(2, retryAttempt))))
.AddTransientHttpErrorPolicy(policyBuilder =>
    policyBuilder.CircuitBreakerAsync(5, TimeSpan.FromSeconds(30)));
```

**Acceptance Criteria:**
- ✅ Service retries on transient failures (502, 503, 504)
- ✅ Circuit breaker opens after 5 consecutive failures
- ✅ Circuit breaker closes after 30 seconds

---

### Phase 1.3: Validation and Testing

#### 1. Contract Testing

**Test Product Catalog Service API:**
```csharp
[Fact]
public async Task GetProducts_ReturnsActiveProducts()
{
    var response = await _client.GetAsync("/api/products");
    
    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var products = await response.Content.ReadFromJsonAsync<List<Product>>();
    products.Should().NotBeEmpty();
    products.Should().OnlyContain(p => p.IsActive);
}

[Fact]
public async Task GetProduct_WithValidId_ReturnsProduct()
{
    var response = await _client.GetAsync("/api/products/1");
    
    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var product = await response.Content.ReadFromJsonAsync<Product>();
    product.Id.Should().Be(1);
}
```

**Acceptance Criteria:**
- ✅ All API contract tests pass
- ✅ Tests run in CI/CD pipeline

---

#### 2. Load Testing

**Scenario**: 100 concurrent users browsing products for 5 minutes

**K6 Script** (`/tests/load/product-catalog.js`):
```javascript
import http from 'k6/http';
import { check, sleep } from 'k6';

export let options = {
  vus: 100,
  duration: '5m',
};

export default function () {
  let response = http.get('http://retail.local/Products');
  
  check(response, {
    'status is 200': (r) => r.status === 200,
    'response time < 500ms': (r) => r.timings.duration < 500,
  });
  
  sleep(1);
}
```

**Run:**
```bash
k6 run tests/load/product-catalog.js
```

**Acceptance Criteria:**
- ✅ p95 response time < 500ms (compare to baseline)
- ✅ Error rate < 1%
- ✅ No database connection pool exhaustion

---

#### 3. Canary Deployment (Gradual Rollout)

**Strategy**: Route 10% of traffic to new service, monitor, then increase to 100%

**Ingress Configuration** (NGINX annotation for weighted routing):
```yaml
apiVersion: networking.k8s.io/v1
kind: Ingress
metadata:
  name: products-canary
  annotations:
    nginx.ingress.kubernetes.io/canary: "true"
    nginx.ingress.kubernetes.io/canary-weight: "10"  # Start with 10%
spec:
  rules:
  - host: retail.local
    http:
      paths:
      - path: /Products
        pathType: Prefix
        backend:
          service:
            name: product-catalog-service
            port:
              number: 8081
```

**Gradual Rollout:**
1. **Day 1**: 10% traffic → monitor for 24 hours
2. **Day 2**: If healthy, increase to 50%
3. **Day 3**: If healthy, increase to 100%
4. **Day 4**: Remove monolith fallback code

**Metrics to Monitor:**
- Error rate (should be < 1%)
- p95 latency (should be ≤ baseline)
- Database load (should be unchanged)
- User complaints (should be zero)

**Acceptance Criteria:**
- ✅ 100% traffic routed to Product Catalog Service
- ✅ No increase in error rate or latency
- ✅ Monolith fallback code removed from Web BFF

---

### Phase 1.4: Documentation and Handoff

**Update Documentation:**
- `/docs/Target-Architecture.md`: Mark Product Catalog Service as "Deployed"
- `/docs/Runbook.md`: Add Product Catalog Service operations section
- Create `/docs/services/ProductCatalogService.md`: API documentation

**Handoff to Operations:**
- Service health check URLs
- Troubleshooting guide (common errors, log locations)
- Runbook for scaling replicas

---

### Slice 1 Exit Criteria

**Before proceeding to Slice 2, verify:**

- ✅ Product Catalog Service deployed and serving 100% of product traffic
- ✅ All tests passing (unit, integration, load)
- ✅ Metrics match or exceed baseline
- ✅ Distributed tracing shows Web BFF → Product Catalog Service
- ✅ Rollback tested and documented
- ✅ Team confident with deployment process
- ✅ Documentation updated

**Artifacts Delivered:**
- `/services/ProductCatalogService/` (source code)
- `/k8s/product-catalog/` (Kubernetes manifests)
- `/docs/services/ProductCatalogService.md` (API docs)
- Updated Web BFF code (Products/Index.cshtml.cs)

**Rollback Plan (if needed):**
1. Change Ingress to route `/Products` back to monolith
2. Revert Web BFF code changes (restore direct database access)
3. Keep Product Catalog Service running (for learning/future retry)

---

## Slice 2: Extract Cart Service

### Objective

Extract the Cart domain as a fully independent service with exclusive ownership of Carts and CartLines tables.

### Why This Slice Second?

✅ **Well-isolated domain**: Cart has clear boundaries
✅ **Existing service interface**: ICartService already abstracts logic
✅ **Short-lived data**: Carts are temporary, low risk of data loss
✅ **Independent scaling**: Cart operations are high-frequency, benefit from separate scaling
✅ **Enables Slice 3**: Checkout Service will call Cart Service via API

### Duration
**3-4 weeks**

### Key Activities

1. **Create Cart Service** (ASP.NET Core Web API)
   - Endpoints: GET, POST, DELETE for cart operations
   - Reuse existing CartService.cs logic
   - Connect to shared database (Carts, CartLines tables)

2. **Deploy to Kubernetes**
   - 3 replicas
   - Health checks
   - Kubernetes Service for internal discovery

3. **Update Web BFF**
   - Replace CartService DI with HttpClient calls to Cart Service
   - Pages/Cart/Index.cshtml.cs → calls `/api/carts/{customerId}`
   - Pages/Products/Index.cshtml.cs (add to cart) → calls `POST /api/carts/{customerId}/items`

4. **Update Checkout Service** (in monolith)
   - CheckoutService calls Cart Service API instead of direct database
   - Ensures no direct Carts table access remains in monolith

5. **Gradual Rollout**
   - Canary deployment (10% → 50% → 100%)
   - Monitor cart operations, checkout success rate

6. **Remove Cart Logic from Monolith**
   - Delete CartService.cs from monolith
   - Remove Pages/Cart from monolith (Web BFF now owns UI)

### Risks

| Risk | Mitigation |
|------|------------|
| **Network latency** (API call vs. in-process) | Cache cart in Web BFF session, load test |
| **Service unavailability** | Circuit breaker, fallback to empty cart |
| **Data inconsistency** | Shared database ensures consistency during transition |

### Exit Criteria

- ✅ Cart Service deployed and serving 100% of cart traffic
- ✅ Checkout flow works end-to-end (add to cart, checkout, order created)
- ✅ Cart logic removed from monolith
- ✅ Load tests pass

---

## Slice 3: Extract Checkout Service (Saga Pattern)

### Objective

Extract the Checkout domain as an orchestrator service that coordinates cart retrieval, payment processing, inventory reservation, and order creation using the **Saga pattern**.

### Why This Slice Third?

✅ **High-value**: Checkout is the critical business flow
✅ **Complex orchestration**: Demonstrates saga pattern for distributed transactions
✅ **Event publishing**: Introduces asynchronous communication
✅ **Depends on Cart Service**: Must complete Slice 2 first

### Duration
**4-5 weeks** (includes saga implementation and testing)

### Key Activities

1. **Create Checkout Service** (ASP.NET Core Web API)
   - Endpoint: `POST /api/checkout`
   - Implements saga orchestrator pattern
   - Publishes events: OrderCreated, PaymentProcessed, InventoryReserved

2. **Integrate Azure Service Bus**
   - Configure topics: orders, payments, inventory
   - Publish events from Checkout Service
   - Consumers: Order Service (future), Analytics (future)

3. **Implement Saga Steps**
   - Step 1: Retrieve cart (call Cart Service API)
   - Step 2: Reserve inventory (call Inventory Service API or database directly)
   - Step 3: Process payment (call Payment Gateway)
   - Step 4: Create order (call Order Service API or database directly)
   - Step 5: Clear cart (call Cart Service API)
   - Step 6: Publish OrderCreated event

4. **Compensation Logic** (rollback on failure)
   - If payment fails: Release inventory reservation, publish CheckoutFailed
   - If order creation fails: Refund payment (future), release inventory

5. **Update Web BFF**
   - Pages/Checkout/Index.cshtml.cs → calls Checkout Service API

6. **Testing**
   - Chaos testing: Kill services mid-checkout, verify rollback
   - Load testing: 50 concurrent checkouts

### Risks

| Risk | Mitigation |
|------|------------|
| **Partial failure** (payment succeeded, order creation failed) | Saga compensation logic, idempotent operations |
| **Event delivery failure** | Service Bus retries, dead-letter queue monitoring |
| **Latency increase** | Async event publishing (don't wait for consumers) |

### Exit Criteria

- ✅ Checkout Service deployed and handling 100% of checkout traffic
- ✅ Saga pattern tested (happy path and failure scenarios)
- ✅ Events published to Service Bus
- ✅ Checkout flow end-to-end tested
- ✅ Compensation logic tested (simulated failures)

---

## Slice 4: Extract Order Service

### Objective

Extract the Order domain as a read-optimized service for order history and details.

### Duration
**2-3 weeks**

### Key Activities

1. **Create Order Service** (ASP.NET Core Web API)
   - Endpoints: GET /api/orders (list), GET /api/orders/{id} (details)
   - Read-only operations (no writes yet)
   - Connect to shared database (Orders, OrderLines tables)

2. **Subscribe to OrderCreated Event** (future)
   - Consume OrderCreated from Service Bus
   - Populate local Orders database (prepare for database split)

3. **Update Web BFF**
   - Pages/Orders/Index.cshtml.cs → calls Order Service API
   - Pages/Orders/Details.cshtml.cs → calls Order Service API

4. **Gradual Rollout**
   - Canary deployment

### Exit Criteria

- ✅ Order Service deployed
- ✅ Order pages load from Order Service API
- ✅ Order writes still in Checkout Service (or monolith)

---

## Slice 5: Database Split - Carts to Redis (Optional Optimization)

### Objective

Move Cart Service from SQL Server to Redis for performance optimization (carts are ephemeral, high-write).

### Duration
**2 weeks**

### Key Activities

1. **Deploy Redis to Kubernetes**
2. **Update Cart Service** to use StackExchange.Redis
3. **Implement TTL** (cart expires after 72 hours)
4. **Dual-write temporarily** (Redis + SQL for safety)
5. **Cutover to Redis-only**

### Exit Criteria

- ✅ Cart Service using Redis
- ✅ Performance improvement (lower latency, higher throughput)

---

## Slice 6: Database Split - Orders Database

### Objective

Give Order Service its own dedicated database (split from shared database).

### Duration
**3-4 weeks**

### Key Activities

1. **Create new Orders database**
2. **Replicate data** from shared database
3. **Switch Order Service** to new database
4. **Checkout Service writes** to Order Service via API (not direct database)
5. **Remove Orders/OrderLines tables** from shared database (after validation period)

### Exit Criteria

- ✅ Order Service owns Orders database
- ✅ No shared database access for orders

---

## Slice 7: Database Split - Products Database

### Objective

Give Product Catalog Service its own dedicated database.

### Duration
**2-3 weeks**

### Key Activities

1. **Create new Products database**
2. **Replicate data** (Products, Inventory tables)
3. **Switch Product Catalog Service** to new database
4. **Remove Products/Inventory tables** from shared database

### Exit Criteria

- ✅ Product Catalog Service owns Products database
- ✅ Shared database fully decommissioned

---

## Slice 8: Web BFF Enhancements (Authentication, Session Management)

### Objective

Replace hardcoded "guest" with proper session-based customer IDs, prepare for user authentication.

### Duration
**3-4 weeks**

### Key Activities

1. **Implement session-based customer IDs** (Guid per session)
2. **Add ASP.NET Core Identity** (optional: user registration/login)
3. **Update all services** to use dynamic customer IDs
4. **Add authorization checks** (users can only see own orders)

### Exit Criteria

- ✅ No more shared "guest" cart
- ✅ Multi-user support
- ✅ Session-based or authenticated customers

---

## Timeline Summary

| Slice | Name | Duration | Cumulative | Key Milestone |
|-------|------|----------|------------|---------------|
| **0** | Preparation & Foundation | 2-3 weeks | 2-3 weeks | Monolith in K8s, baseline metrics |
| **1** | Product Catalog Service (Read-Only) | 3-4 weeks | 5-7 weeks | First service extracted |
| **2** | Cart Service | 3-4 weeks | 8-11 weeks | Second service, cart isolated |
| **3** | Checkout Service (Saga) | 4-5 weeks | 12-16 weeks | Saga pattern, event publishing |
| **4** | Order Service | 2-3 weeks | 14-19 weeks | Four services running |
| **5** | Carts to Redis (Optional) | 2 weeks | 16-21 weeks | Performance optimization |
| **6** | Database Split - Orders | 3-4 weeks | 19-25 weeks | Database per service begins |
| **7** | Database Split - Products | 2-3 weeks | 21-28 weeks | Shared DB decommissioned |
| **8** | Authentication & Sessions | 3-4 weeks | 24-32 weeks | Multi-user support |

**Total Duration**: 24-32 weeks (6-8 months)

---

## Post-Migration: Continuous Improvement

Once all slices are complete, focus shifts to optimization:

1. **Service Mesh** (Istio or Linkerd): mTLS, advanced traffic management
2. **CQRS**: Separate read/write models for Order Service
3. **Event Sourcing**: Audit trail for order lifecycle
4. **GraphQL Gateway**: Unified API for mobile/SPA clients
5. **Multi-region Deployment**: Geographic distribution
6. **Auto-scaling**: HPA based on custom metrics (queue depth, RPS)

---

## Governance and Decision Gates

### Before Starting Each Slice

- ✅ **Slice Planning Review**: Team understands scope, risks, acceptance criteria
- ✅ **Previous Slice Validated**: All exit criteria met
- ✅ **Resources Allocated**: Developers assigned, time blocked

### During Each Slice

- **Daily Standups**: Progress, blockers, coordination
- **Mid-Slice Check-In**: Review metrics, adjust plan if needed
- **Pair Programming**: For risky components (saga compensation, database changes)

### After Each Slice

- ✅ **Demo to Stakeholders**: Show working functionality
- ✅ **Retrospective**: What went well, what to improve
- ✅ **Documentation Review**: Ensure runbooks updated
- ✅ **Go/No-Go Decision**: Approve proceeding to next slice

---

## Success Metrics

**Technical Metrics:**
- ✅ Zero production incidents caused by migration
- ✅ p95 latency ≤ baseline (no performance regression)
- ✅ 99.9% uptime maintained throughout migration
- ✅ All services independently deployable

**Business Metrics:**
- ✅ Conversion rate (checkout completion) unchanged or improved
- ✅ Customer complaints about performance: zero
- ✅ Time-to-deploy new features reduced (after migration complete)

**Team Metrics:**
- ✅ Team confidence in microservices architecture (survey)
- ✅ Deployment frequency increased (measure before/after)
- ✅ Mean time to recovery (MTTR) decreased (easier rollback)

---

## Conclusion

This migration plan provides a safe, incremental path from monolith to microservices using the Strangler Fig pattern. Each slice is independently valuable, testable, and reversible. By starting with the low-risk Product Catalog Service (Slice 1), the team builds confidence and establishes patterns for subsequent slices.

**The plan is achievable** because:
- ✅ Uses existing .NET 8 and SQL Server stack
- ✅ Preserves all current functionality
- ✅ Allows learning and adjustment between slices
- ✅ Provides rollback capability at every step
- ✅ Delivers value incrementally (no waiting 6 months to see results)

**Key to success**:
- Start small (Slice 0 and Slice 1 are foundational)
- Measure everything (baseline metrics are critical)
- Communicate constantly (team alignment prevents surprises)
- Embrace learning (first slice will be hardest, subsequent slices faster)

**Next Steps**:
1. **Review and approve Slice 0 and Slice 1** (focus on first extraction)
2. **Assemble team** (3-5 developers, 1 DevOps engineer)
3. **Begin Slice 0** (containerization and infrastructure setup)
4. **Celebrate each milestone** (completed slices are achievements!)

---

**Related Documents:**
- `/docs/Target-Architecture.md` - Detailed target state
- `/docs/ADR/` - Architectural decision records
- `/docs/Runbook.md` - Operational procedures
