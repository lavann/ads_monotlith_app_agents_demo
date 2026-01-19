# ADR-001: Using Entity Framework Core with SQL Server LocalDB

## Status
Accepted

## Context
The RetailMonolith application requires persistent storage for products, inventory, shopping carts, and orders. The team needed to select a data access technology and database engine that would:

- Enable rapid development with minimal configuration
- Support development on Windows/Visual Studio environments
- Provide strong typing and compile-time query validation
- Allow easy schema evolution through migrations
- Work well with the ASP.NET Core ecosystem

The application is designed as a demonstration/prototype system for showcasing modernisation patterns, prioritizing developer productivity and ease of setup over production-grade database requirements.

## Decision
We will use **Entity Framework Core 9.0.9** as the ORM with **SQL Server LocalDB** as the database engine for development.

### Implementation Details

1. **Entity Framework Core (EF Core)**
   - Microsoft's modern ORM for .NET
   - Code-first approach with POCO entities
   - Integrated with ASP.NET Core's dependency injection
   - Design-time factory for migrations: `DesignTimeDbContextFactory`

2. **SQL Server LocalDB**
   - Lightweight SQL Server Express edition
   - Runs on-demand without service management
   - Connection string: `Server=(localdb)\\MSSQLLocalDB;Database=RetailMonolith;Trusted_Connection=True;MultipleActiveResultSets=true`
   - Bundled with Visual Studio (no separate installation for many developers)

3. **Database Management**
   - Automatic migrations on application startup: `await db.Database.MigrateAsync()`
   - Automatic seeding of sample data: `await AppDbContext.SeedAsync(db)`
   - Migration files version-controlled in `/Migrations` directory

4. **Configuration**
   - Default connection string in `Program.cs`
   - Override via `ConnectionStrings__DefaultConnection` environment variable
   - Supports swapping to Azure SQL or other SQL Server instances for production

## Consequences

### Positive
- **Zero configuration for local development**: LocalDB auto-installs with Visual Studio
- **Type safety**: LINQ queries are checked at compile time
- **Schema versioning**: Migrations provide clear evolution path
- **Rich query capabilities**: EF Core supports complex joins, includes, projections
- **Developer productivity**: No manual SQL for CRUD operations
- **Easy testing**: In-memory database provider available for unit tests

### Negative
- **Windows-centric**: LocalDB requires Windows OS (though EF Core itself is cross-platform)
- **LocalDB limitations**: Not suitable for production workloads, limited connection pooling
- **EF Core learning curve**: Requires understanding of tracking, includes, projections
- **Performance overhead**: ORM abstraction can be slower than raw SQL for bulk operations
- **N+1 query risk**: Easy to introduce inefficient query patterns (e.g., `CheckoutService` inventory loop)

### Risks
- **Portability**: LocalDB ties development to Windows without Docker setup
- **Production mismatch**: LocalDB behavior may differ from Azure SQL or full SQL Server
- **Migration complexity**: Schema changes require careful migration authoring and testing
- **Connection string management**: Sensitive credentials may be needed in production

### Mitigation Strategies (Observed in Code)
- Environment variable override allows switching database providers
- Hardcoded connection string only in design-time factory (not runtime)
- Multiple active result sets enabled for complex query scenarios
- Unique indexes on SKU fields prevent data integrity issues

## Notes
The code includes `Microsoft.Extensions.Http.Polly` (9.0.9) in dependencies but doesn't currently use it. This may indicate planned resilience for database connections or external service calls.

The `MultipleActiveResultSets=true` setting suggests awareness of potential nested query scenarios, though current code doesn't appear to require it.

## Related Decisions
- Future: Consider adding Dapper for read-heavy queries (order history, product catalog)
- Future: Evaluate distributed database patterns if decomposing to microservices
