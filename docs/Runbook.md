# Runbook: RetailMonolith Application

## Overview
Operational guide for building, running, and troubleshooting the RetailMonolith ASP.NET Core 8 application.

---

## Prerequisites

### Required Software
- **.NET 8 SDK**: Download from [dotnet.microsoft.com](https://dotnet.microsoft.com/download/dotnet/8.0)
- **SQL Server LocalDB**: Included with Visual Studio or available as standalone install

### Verify Installation
```bash
# Check .NET version
dotnet --version
# Should show 8.0.x or higher

# Check SQL Server LocalDB (Windows only)
sqllocaldb info
# Should list available instances
```

### Optional Tools
- **Visual Studio 2022** (17.8+) or **Visual Studio Code** with C# extension
- **SQL Server Management Studio (SSMS)** or **Azure Data Studio** for database inspection
- **Git** for version control

---

## Local Development Setup

### 1. Clone Repository
```bash
git clone https://github.com/lavann/ads_monotlith_app.git
cd ads_monotlith_app
```

### 2. Restore Dependencies
```bash
dotnet restore
```

**Expected Output**:
```
Restore completed in X ms for RetailMonolith.csproj
```

**Troubleshooting**:
- If restore fails, check internet connection (NuGet package download)
- Clear NuGet cache: `dotnet nuget locals all --clear`

### 3. Apply Database Migrations
```bash
dotnet ef database update
```

**Expected Output**:
```
Build started...
Build succeeded.
Applying migration '20251019185248_Initial'.
Done.
```

**Troubleshooting**:
- If `dotnet ef` not found: `dotnet tool install --global dotnet-ef`
- If connection fails: Ensure SQL Server LocalDB service is running
- Manual service start: `sqllocaldb start MSSQLLocalDB`

### 4. Run Application
```bash
dotnet run
```

**Expected Output**:
```
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: http://localhost:5000
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: https://localhost:5001
```

### 5. Access Application
- **HTTP**: http://localhost:5000
- **HTTPS**: https://localhost:5001
- **Health Check**: https://localhost:5001/health

**First Run Behavior**:
- Database is automatically migrated
- 50 sample products are seeded (if database is empty)
- Seeding may take 5-10 seconds

---

## Common Commands

### Build
```bash
# Build without running
dotnet build

# Build in Release mode
dotnet build -c Release
```

### Run
```bash
# Run with hot reload (file watch)
dotnet watch run

# Run in production mode
dotnet run --environment Production

# Specify URLs
dotnet run --urls "http://localhost:8080;https://localhost:8443"
```

### Database Management

#### View Migration History
```bash
dotnet ef migrations list
```

#### Create New Migration
```bash
# After modifying Models/*.cs files
dotnet ef migrations add <MigrationName>

# Example
dotnet ef migrations add AddProductImageUrl
```

#### Revert Migration
```bash
# Revert to specific migration
dotnet ef database update <PreviousMigrationName>

# Example
dotnet ef database update Initial
```

#### Reset Database (Fresh Start)
```bash
# Drop database
dotnet ef database drop -f

# Recreate and seed
dotnet ef database update
dotnet run
```

**Warning**: Drops all data! Use only in development.

#### Manual Database Connection
**Connection String**:
```
Server=(localdb)\MSSQLLocalDB;Database=RetailMonolith;Trusted_Connection=True;MultipleActiveResultSets=true
```

**Using SSMS**:
1. Open SQL Server Management Studio
2. Server name: `(localdb)\MSSQLLocalDB`
3. Authentication: Windows Authentication
4. Connect to database: `RetailMonolith`

**Useful Queries**:
```sql
-- View all products
SELECT * FROM Products WHERE IsActive = 1;

-- Check inventory levels
SELECT * FROM Inventory ORDER BY Quantity ASC;

-- View recent orders
SELECT * FROM Orders ORDER BY CreatedUtc DESC;

-- Cart contents
SELECT c.CustomerId, cl.Name, cl.Quantity, cl.UnitPrice
FROM Carts c
INNER JOIN CartLines cl ON c.Id = cl.CartId;
```

### Testing (No tests currently exist)
```bash
# If tests are added in the future
dotnet test
```

### Publish for Deployment
```bash
# Self-contained Windows deployment
dotnet publish -c Release -r win-x64 --self-contained

# Framework-dependent deployment (smaller size)
dotnet publish -c Release

# Output location: bin/Release/net8.0/publish/
```

---

## Configuration

### Environment Variables

Set before running the application:

**Windows (PowerShell)**:
```powershell
$env:ConnectionStrings__DefaultConnection="Server=myserver;Database=RetailMonolith;User Id=sa;Password=xxx;"
dotnet run
```

**Windows (Command Prompt)**:
```cmd
set ConnectionStrings__DefaultConnection=Server=myserver;Database=RetailMonolith;User Id=sa;Password=xxx;
dotnet run
```

**Linux/macOS (Bash)**:
```bash
export ConnectionStrings__DefaultConnection="Server=myserver;Database=RetailMonolith;User Id=sa;Password=xxx;"
dotnet run
```

### Common Configuration Overrides

| Variable | Purpose | Example |
|----------|---------|---------|
| `ConnectionStrings__DefaultConnection` | Database connection | See above |
| `ASPNETCORE_ENVIRONMENT` | Environment name | `Production`, `Staging`, `Development` |
| `ASPNETCORE_URLS` | Listen addresses | `http://0.0.0.0:8080` |
| `Logging__LogLevel__Default` | Log verbosity | `Debug`, `Information`, `Warning` |

### appsettings.json Modification
Avoid modifying `appsettings.json` directly. Instead:
1. Create `appsettings.Development.json` for local overrides
2. Use environment variables for deployment-specific settings
3. Use Azure App Configuration or Key Vault for production secrets

---

## Application Endpoints

### User-Facing Pages (Razor Pages)

| URL | Description | Method |
|-----|-------------|--------|
| `/` | Home page | GET |
| `/Products` | Product catalog | GET |
| `/Products?handler=...` | Add to cart | POST |
| `/Cart` | Shopping cart | GET |
| `/Checkout` | Checkout form | GET |
| `/Checkout` | Process order | POST |
| `/Orders` | Order history | GET |
| `/Orders/Details?id={id}` | Order details | GET |
| `/Privacy` | Privacy policy | GET |
| `/Error` | Error page | GET |

### API Endpoints

| URL | Method | Request Body | Response | Description |
|-----|--------|--------------|----------|-------------|
| `/api/checkout` | POST | None | `{ id, status, total }` | Process checkout for "guest" |
| `/api/orders/{id}` | GET | None | Full order object | Get order by ID |
| `/health` | GET | None | Health status | Health check |

**Example API Usage**:
```bash
# Checkout (creates order from guest's cart)
curl -X POST https://localhost:5001/api/checkout

# Get order details
curl https://localhost:5001/api/orders/1
```

---

## Known Issues & Tech Debt

### Critical Issues

1. **Shared Guest Cart** (ADR-004)
   - **Issue**: All users share the same cart (CustomerId = "guest")
   - **Impact**: Concurrent users will see each other's cart items
   - **Workaround**: Use in single-user/demo scenarios only
   - **Fix Required**: Implement session-based customer IDs before production

2. **Mock Payment Gateway** (ADR-003)
   - **Issue**: All payments always succeed
   - **Impact**: Cannot test payment failure scenarios
   - **Workaround**: None (code path exists but never triggered)
   - **Fix Required**: Replace with real payment gateway or enhanced mock

3. **No Authentication/Authorization**
   - **Issue**: No user login, anyone can view any order
   - **Impact**: Privacy violation, no audit trail
   - **Workaround**: None
   - **Fix Required**: Implement ASP.NET Core Identity or similar

### Medium Priority Issues

4. **Inventory Reservation Race Condition**
   - **Issue**: Checkout queries inventory in loop (N+1 pattern)
   - **Code**: `CheckoutService.CheckoutAsync()` line 28-32
   - **Impact**: Concurrent checkouts may oversell inventory
   - **Workaround**: Use database-level locking or pessimistic concurrency

5. **No Pagination on Orders**
   - **Issue**: `/Orders` loads all orders into memory
   - **Code**: `Pages/Orders/Index.cshtml.cs` line 18-21
   - **Impact**: Performance degrades with large order history
   - **Workaround**: Limit by date range manually via SQL

6. **Commented-Out Code**
   - **Issue**: Dead code in Products/Index.OnPostAsync
   - **Code**: Lines 58-61 and 63-69 have duplicate/unused logic
   - **Impact**: Confusing to maintainers
   - **Workaround**: None needed
   - **Fix**: Remove commented code, use CartService exclusively

### Low Priority Issues

7. **Hardcoded Currency (GBP)**
   - **Issue**: No multi-currency support
   - **Impact**: Cannot sell in other currencies
   - **Workaround**: Change hardcoded values in `AppDbContext.SeedAsync()` and `CheckoutService`

8. **No Logging of Business Events**
   - **Issue**: No structured logging for checkout, payment, inventory changes
   - **Impact**: Difficult to debug issues or track business metrics
   - **Workaround**: Use SQL queries to analyze database state

9. **No Caching**
   - **Issue**: Every page load queries database
   - **Impact**: Higher database load, slower responses
   - **Workaround**: Enable SQL Server query plan caching

10. **Empty XML Documentation**
    - **Issue**: `DesignTimeDbContextFactory` has empty `<summary>` tags
    - **Impact**: IDE tooltips unhelpful
    - **Workaround**: Read code directly

---

## Troubleshooting Guide

### Application Won't Start

**Symptom**: `dotnet run` fails immediately

**Possible Causes**:
1. **Port already in use**
   - Check: `netstat -ano | findstr :5000` (Windows) or `lsof -i :5000` (Linux/macOS)
   - Fix: Kill process or change port via `--urls` parameter

2. **Database connection fails**
   - Check: SQL Server LocalDB service status
   - Fix: `sqllocaldb start MSSQLLocalDB`

3. **Missing dependencies**
   - Check: `dotnet restore` output
   - Fix: Restore packages: `dotnet restore`

### Database Errors

**Symptom**: `SqlException` or `The database does not exist`

**Solution**:
```bash
# Ensure LocalDB is running
sqllocaldb info
sqllocaldb start MSSQLLocalDB

# Drop and recreate database
dotnet ef database drop -f
dotnet ef database update
```

**Symptom**: `PendingModelChanges` error

**Solution**:
```bash
# Create migration for model changes
dotnet ef migrations add <DescriptiveName>
dotnet ef database update
```

### Slow First Request

**Symptom**: First page load after startup takes 10-30 seconds

**Cause**: Database migration and seeding run on startup (50 products inserted)

**Expected Behavior**: Subsequent requests are fast

**Optimization**: Pre-seed database, disable auto-migration in production

### Cart Items Disappear

**Symptom**: Added items vanish from cart

**Cause 1**: Another user cleared the cart (shared "guest" cart)
**Cause 2**: Database was reset

**Workaround**: Add items again (demo limitation)

### Checkout Fails

**Symptom**: Error during checkout POST

**Common Causes**:
1. **Empty cart**: Ensure cart has items
2. **Insufficient inventory**: Check `Inventory` table quantities
3. **Database lock**: Concurrent checkout may cause deadlock

**Debug Steps**:
```sql
-- Check cart contents
SELECT * FROM CartLines WHERE CartId IN (SELECT Id FROM Carts WHERE CustomerId = 'guest');

-- Check inventory
SELECT * FROM Inventory WHERE Sku IN (SELECT Sku FROM CartLines);
```

### LocalDB Not Available (Non-Windows)

**Issue**: LocalDB is Windows-only

**Solutions**:
1. **Use Docker SQL Server**:
   ```bash
   docker run -e "ACCEPT_EULA=Y" -e "SA_PASSWORD=YourStrong@Passw0rd" -p 1433:1433 -d mcr.microsoft.com/mssql/server:2022-latest
   ```
   Update connection string: `Server=localhost,1433;Database=RetailMonolith;User Id=sa;Password=YourStrong@Passw0rd;TrustServerCertificate=True`

2. **Use SQLite** (requires code changes):
   - Replace `Microsoft.EntityFrameworkCore.SqlServer` with `Microsoft.EntityFrameworkCore.Sqlite`
   - Update `Program.cs`: `UseSqlite("Data Source=retailmonolith.db")`

---

## Monitoring & Health Checks

### Health Check Endpoint
```bash
curl https://localhost:5001/health
```

**Expected Response**: HTTP 200 with "Healthy" status

**Integration**:
- Azure App Service: Configure health check path to `/health`
- Kubernetes: Use as liveness/readiness probe
- Load balancers: Use for backend health monitoring

### Logging

**Current Configuration** (appsettings.json):
- Default: Information level
- ASP.NET Core: Warning level

**View Logs**:
- Console output during `dotnet run`
- Use structured logging providers in production (Application Insights, Serilog)

**Increase Verbosity**:
```bash
dotnet run --Logging:LogLevel:Default=Debug
```

---

## Deployment Considerations

### Azure App Service Deployment

1. **Connection String**: Set in Configuration → Connection strings
2. **Environment**: Set `ASPNETCORE_ENVIRONMENT=Production`
3. **Health Check**: Configure to `/health`
4. **SQL Database**: Migrate from LocalDB to Azure SQL Database

### Docker Deployment

**Dockerfile** (not included, create if needed):
```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80

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

**Build and Run**:
```bash
docker build -t retailmonolith .
docker run -p 8080:80 -e ConnectionStrings__DefaultConnection="..." retailmonolith
```

---

## Security Checklist Before Production

- [ ] Replace mock payment gateway with real provider
- [ ] Implement authentication (ASP.NET Core Identity)
- [ ] Add authorization checks on sensitive endpoints
- [ ] Replace hardcoded "guest" with user-specific IDs
- [ ] Enable HTTPS only (remove HTTP endpoint)
- [ ] Configure HSTS with long max-age
- [ ] Store connection strings in Azure Key Vault or similar
- [ ] Add API rate limiting
- [ ] Enable audit logging for orders and payments
- [ ] Implement CSRF protection (already present, verify enabled)
- [ ] Add input validation on API endpoints
- [ ] Configure CORS appropriately if exposing APIs
- [ ] Review and test error handling (avoid leaking stack traces)

---

## Support & Resources

### Internal Documentation
- [High-Level Design (HLD)](./HLD.md)
- [Low-Level Design (LLD)](./LLD.md)
- [Architecture Decision Records](./ADR/)

### External References
- [ASP.NET Core Documentation](https://docs.microsoft.com/aspnet/core)
- [Entity Framework Core Documentation](https://docs.microsoft.com/ef/core)
- [.NET 8 Download](https://dotnet.microsoft.com/download/dotnet/8.0)
- [SQL Server LocalDB Documentation](https://docs.microsoft.com/sql/database-engine/configure-windows/sql-server-express-localdb)

### Getting Help
- Check logs in console output
- Review [Known Issues](#known-issues--tech-debt) section
- Inspect database state using SQL queries above
- Consult LLD.md for code-level details
