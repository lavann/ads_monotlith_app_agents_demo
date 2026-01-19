# RetailMonolith Documentation

## Overview
This directory contains comprehensive documentation for the RetailMonolith application - an ASP.NET Core 8 e-commerce demonstration system designed to showcase modernisation and microservices decomposition patterns.

## Documentation Structure

### 📘 [High-Level Design (HLD.md)](./HLD.md)
System-level architecture and design overview:
- System overview and architecture style
- Core components and domain boundaries (Products, Cart, Checkout, Orders)
- Data stores and external dependencies
- API endpoints and runtime assumptions
- Configuration and monitoring
- Security considerations and future decomposition readiness

**Audience**: Technical leads, architects, stakeholders

### 📗 [Low-Level Design (LLD.md)](./LLD.md)
Detailed technical implementation guide:
- Module structure and organization
- Data layer (EF Core context, entities)
- Domain models (Product, Cart, Order, Inventory)
- Service layer (CartService, CheckoutService, PaymentGateway)
- Presentation layer (Razor Pages)
- Request flows and sequence diagrams
- Areas of coupling and hotspots
- Technical debt catalog
- Extensibility points

**Audience**: Developers, code reviewers, maintainers

### 📕 [Runbook (Runbook.md)](./Runbook.md)
Operational procedures and troubleshooting:
- Prerequisites and setup instructions
- Build, run, and deployment commands
- Database management (migrations, seeding, reset)
- Configuration and environment variables
- All application endpoints
- Known issues and technical debt
- Troubleshooting guide
- Monitoring and health checks
- Security checklist

**Audience**: DevOps engineers, SREs, developers

### 📂 [Architecture Decision Records (ADR/)](./ADR/)
Historical record of architectural choices:

#### [ADR-001: Entity Framework Core with SQL Server LocalDB](./ADR/ADR-001-entity-framework-core-sqlserver-localdb.md)
- **Decision**: Use EF Core 9.0.9 with LocalDB for data access
- **Rationale**: Rapid development, zero configuration, type safety
- **Tradeoffs**: Windows-centric, LocalDB unsuitable for production

#### [ADR-002: Monolithic Razor Pages Architecture](./ADR/ADR-002-monolithic-razor-pages-architecture.md)
- **Decision**: Server-side Razor Pages in monolithic deployment
- **Rationale**: Simple deployment, easy debugging, demonstration purposes
- **Tradeoffs**: Limited scalability, technology coupling

#### [ADR-003: Mock Payment Gateway](./ADR/ADR-003-mock-payment-gateway.md)
- **Decision**: Mock payment gateway that always succeeds
- **Rationale**: Safe development, no external dependencies
- **Tradeoffs**: Not production-ready, can't test failure paths

#### [ADR-004: Guest-Based Session Management](./ADR/ADR-004-guest-based-session-management.md)
- **Decision**: Hardcoded "guest" customer ID for all operations
- **Rationale**: Simplify demo, focus on architecture patterns
- **Tradeoffs**: No multi-user support, privacy issues, must replace before production

**Audience**: Architects, technical decision makers, future maintainers

---

## Quick Navigation

### For New Developers
1. Start with [Runbook.md](./Runbook.md) - Local Development Setup
2. Review [HLD.md](./HLD.md) - System Overview
3. Deep dive into [LLD.md](./LLD.md) - Module Overview

### For Architects
1. Review [HLD.md](./HLD.md) - Architecture Style
2. Read all [ADRs](./ADR/) to understand design decisions
3. Check [LLD.md](./LLD.md) - Areas of Coupling

### For DevOps/SRE
1. Follow [Runbook.md](./Runbook.md) - Deployment Considerations
2. Review [HLD.md](./HLD.md) - External Dependencies
3. Check [Runbook.md](./Runbook.md) - Monitoring & Health Checks

### For Troubleshooting
1. Check [Runbook.md](./Runbook.md) - Known Issues & Troubleshooting Guide
2. Review [LLD.md](./LLD.md) - Technical Debt
3. Consult relevant ADR for design context

---

## Key Findings Summary

### Domain Boundaries (Clean Separation)
✅ **Products**: Catalog management, inventory tracking  
✅ **Cart**: Shopping cart lifecycle  
✅ **Checkout**: Order processing, payment orchestration  
✅ **Orders**: Order history and tracking  

### Data Model (Entity Framework Core)
- **6 entity types**: Product, InventoryItem, Cart, CartLine, Order, OrderLine
- **SQL Server LocalDB**: Development database
- **Automatic migrations**: Run on app startup
- **Seeded data**: 50 sample products across 6 categories

### Key Endpoints
- **Razor Pages**: `/Products`, `/Cart`, `/Checkout`, `/Orders`
- **Minimal APIs**: `POST /api/checkout`, `GET /api/orders/{id}`
- **Health Check**: `GET /health`

### Critical Issues Identified
⚠️ **Must fix before production**:
1. Shared "guest" cart (ADR-004) - all users see same cart
2. Mock payment gateway (ADR-003) - always succeeds
3. No authentication/authorization - privacy violation
4. Inventory race condition - concurrent checkout may oversell

### Technical Debt Highlights
- No unit tests
- No pagination on order list
- N+1 query in checkout (inventory loop)
- Commented-out code in Products page
- Direct DbContext injection in some pages (bypassing service layer)

### Future-Ready Features
✅ Service interfaces (ICartService, ICheckoutService, IPaymentGateway)  
✅ Minimal APIs already exposed  
✅ Clear domain boundaries for extraction  
✅ Comments indicating event publishing preparation  

---

## Documentation Standards

### Maintenance
- Update HLD when changing system architecture or adding external dependencies
- Update LLD when modifying modules, services, or data models
- Create new ADR when making architectural decisions
- Update Runbook when changing build/deployment procedures or discovering new issues

### ADR Template
New ADRs should follow this structure:
- **Status**: Accepted/Superseded/Deprecated
- **Context**: What problem are we solving?
- **Decision**: What did we choose and how is it implemented?
- **Consequences**: Positive/negative outcomes, risks, tradeoffs

### Review Cycle
- Review docs during PR reviews for code changes
- Quarterly review of Runbook for accuracy
- Update technical debt section as issues are resolved

---

## Related Resources

### Internal
- [README.md](../README.md) - Project overview and quick start
- [Source Code](../) - Application codebase

### External
- [ASP.NET Core Documentation](https://docs.microsoft.com/aspnet/core)
- [Entity Framework Core Documentation](https://docs.microsoft.com/ef/core)
- [Microservices Patterns](https://microservices.io/patterns/)
- [Architecture Decision Records (ADR) Specification](https://adr.github.io/)

---

## Documentation Metrics

| Document | Lines | Primary Focus |
|----------|-------|---------------|
| HLD.md | 255 | System architecture, components, dependencies |
| LLD.md | 677 | Code structure, flows, coupling, tech debt |
| Runbook.md | 550 | Operations, commands, troubleshooting |
| ADR-001 | 80 | EF Core + LocalDB decision |
| ADR-002 | 117 | Monolithic architecture decision |
| ADR-003 | 170 | Mock payment gateway decision |
| ADR-004 | 213 | Guest session management decision |
| **Total** | **2,062** | **Complete system documentation** |

---

## Contributing to Documentation

### When to Update Docs
- **HLD**: Adding new services, external dependencies, deployment targets
- **LLD**: Modifying services, entities, request flows, fixing tech debt
- **Runbook**: Changing build process, adding commands, discovering issues
- **ADR**: Making architectural decisions (technology choice, pattern adoption)

### How to Update
1. Make changes to relevant markdown file(s)
2. Verify accuracy against current codebase
3. Update "Last Updated" date (if added to templates)
4. Include doc updates in same PR as code changes

### Style Guide
- Use markdown formatting consistently
- Include code examples where helpful
- Add diagrams using ASCII art or external tools
- Link between documents for cross-references
- Keep language clear and concise

---

*Documentation generated: January 2026*  
*Reflects codebase state as of commit: 4b3a32c*
