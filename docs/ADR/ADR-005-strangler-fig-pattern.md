# ADR-005: Strangler Fig Pattern for Monolith Decomposition

## Status
Accepted

## Context

The RetailMonolith application needs to evolve from a monolithic architecture to microservices to achieve:
- Independent scaling of business capabilities (product catalog, cart, checkout, orders)
- Faster deployment cycles (deploy one service without affecting others)
- Technology diversity (potential to use Redis for carts, different databases per service)
- Team autonomy (separate teams can own separate services)
- Improved resilience (failure in one service doesn't bring down entire application)

However, a migration to microservices carries significant risks:
- **Big-bang rewrites historically fail** (high risk, long time-to-value, behavior changes)
- **Data consistency challenges** (distributed transactions, eventual consistency)
- **Operational complexity** (more moving parts, service discovery, network latency)
- **Team learning curve** (Kubernetes, distributed tracing, saga patterns)
- **Business continuity** (cannot afford downtime or broken functionality during migration)

The team evaluated several migration strategies:

### Alternative 1: Big Bang Rewrite
**Approach**: Rewrite entire application as microservices before deploying

**Pros**:
- Clean slate, no legacy code
- Optimal service boundaries from day one

**Cons**:
- ❌ High risk (months of work before any production deployment)
- ❌ Long time-to-value (no incremental benefits)
- ❌ Difficult to validate behavior equivalence
- ❌ "Second system effect" - over-engineering new system

**Rejected**: Too risky for a business-critical application

### Alternative 2: Branch by Abstraction
**Approach**: Create abstractions within monolith, then split once abstractions stabilized

**Pros**:
- Low risk (changes tested in monolith)
- Gradual code preparation

**Cons**:
- ❌ Monolith grows more complex (abstraction layers add cognitive load)
- ❌ Still requires eventual cutover (abstractions must be extracted)
- ❌ Doesn't reduce deployment coupling until extraction

**Rejected**: Delays actual service extraction too long

### Alternative 3: Parallel Run
**Approach**: Build new services alongside monolith, run both, compare outputs

**Pros**:
- Validation of new system before cutover
- Safe testing in production

**Cons**:
- ❌ Double compute cost (running two systems)
- ❌ Complex routing and comparison logic
- ❌ Still requires eventual cutover decision

**Rejected**: Resource-intensive, doesn't solve cutover risk

## Decision

We will use the **Strangler Fig Pattern** to incrementally migrate from monolith to microservices.

### Pattern Description

The Strangler Fig pattern (named after fig plants that grow around host trees, eventually replacing them) involves:

1. **Identify a cohesive subset** of functionality (bounded context)
2. **Build new service** alongside monolith
3. **Route requests** to new service (facade/gateway pattern)
4. **Validate** new service matches monolith behavior
5. **Incrementally increase traffic** to new service (canary deployment)
6. **Remove functionality** from monolith once 100% migrated
7. **Repeat** for next subset

**Visualization**:
```
Phase 0: Monolith Only
┌─────────────────────────┐
│      Monolith           │
│  - Products             │
│  - Cart                 │
│  - Checkout             │
│  - Orders               │
└─────────────────────────┘

Phase 1: First Service Extracted (Products)
┌─────────────────────────┐      ┌─────────────────────┐
│      Monolith           │      │  Product Catalog    │
│  - Cart                 │◄────►│     Service         │
│  - Checkout             │      └─────────────────────┘
│  - Orders               │
└─────────────────────────┘

Phase 2: Second Service Extracted (Cart)
┌─────────────────────────┐      ┌─────────────────────┐
│      Monolith           │      │  Product Catalog    │
│  - Checkout             │◄────►│     Service         │
│  - Orders               │      └─────────────────────┘
└─────────────────────────┘
                                 ┌─────────────────────┐
                          ◄─────►│   Cart Service      │
                                 └─────────────────────┘

Phase N: Monolith Decommissioned
┌─────────────────────┐  ┌─────────────────────┐  ┌─────────────────────┐
│  Product Catalog    │  │   Cart Service      │  │  Checkout Service   │
│     Service         │  │                     │  │                     │
└─────────────────────┘  └─────────────────────┘  └─────────────────────┘
                                                   ┌─────────────────────┐
                                                   │   Order Service     │
                                                   └─────────────────────┘
```

### Implementation Strategy for RetailMonolith

**Slice Sequence** (based on risk and dependencies):

1. **Slice 1: Product Catalog Service (Read-Only)**
   - **Why first**: Read-only operations, lowest risk, clear boundaries
   - **Data**: Shared database (Products, Inventory tables)
   - **Rollback**: Route traffic back to monolith

2. **Slice 2: Cart Service**
   - **Why second**: Well-isolated domain, existing service interface, short-lived data
   - **Data**: Shared database (Carts, CartLines tables), future: Redis
   - **Rollback**: Route traffic back to monolith

3. **Slice 3: Checkout Service (Saga Pattern)**
   - **Why third**: Complex orchestration, demonstrates saga pattern, high value
   - **Data**: Coordinates multiple services, publishes events
   - **Rollback**: More complex due to distributed transactions, saga compensation logic

4. **Slice 4: Order Service**
   - **Why fourth**: Read-mostly operations, depends on Checkout Service events
   - **Data**: Shared database initially, then dedicated database
   - **Rollback**: Route traffic back to monolith

**Routing Strategy**:
- **Phase 1**: Web BFF (existing Razor Pages) calls new services via HTTP
- **Phase 2**: Gradual rollout using Kubernetes Ingress canary deployments (10% → 50% → 100%)
- **Phase 3**: Remove functionality from monolith once 100% traffic migrated

**Data Strategy**:
- **Phase 1 (Initial)**: Shared database - all services connect to same SQL Server database
- **Phase 2 (Transition)**: Services own tables (enforced by code review, database permissions)
- **Phase 3 (Target)**: Database per service - each service has dedicated database

**Fallback Mechanism**:
- Each Web BFF route has fallback to monolith for 2 weeks after 100% cutover
- Example: If Product Catalog Service returns 5xx, fall back to monolith database query
- Removes fallback after stability validated

## Consequences

### Positive

✅ **Incremental value delivery**
- Each slice delivers working, deployable software (no waiting months for "all or nothing")
- Slice 1 (Product Catalog) can be completed in 3-4 weeks
- Early feedback informs subsequent slices

✅ **Low risk per slice**
- Small scope per slice limits blast radius
- Rollback to previous state is simple (change routing, redeploy monolith)
- Shared database prevents data loss during rollback

✅ **Continuous learning**
- Team builds confidence and skills incrementally
- Mistakes in Slice 1 don't jeopardize entire migration
- Patterns established early (health checks, tracing, deployment) reused in later slices

✅ **Business continuity**
- Application remains functional throughout migration
- Zero planned downtime (blue-green deployments)
- Users unaware of backend changes (behavior preservation)

✅ **Technology flexibility**
- Can introduce new technologies per service (Redis for Cart, events for Checkout)
- Doesn't lock team into upfront decisions (discover needs as you go)

✅ **Team morale**
- Frequent milestones (completed slices) maintain momentum
- Visible progress (each slice is demonstrable)
- Reduced stress compared to big-bang rewrite

### Negative

⚠️ **Longer total timeline**
- Incremental approach takes 6-8 months vs. hypothetical 3-month rewrite
- Multiple deployment cycles (overhead per slice)
- Context switching between slices

⚠️ **Duplication during transition**
- Monolith and services both exist for months (two codebases to maintain)
- Some logic duplicated (e.g., Product queries in monolith and Product Catalog Service)
- Higher infrastructure cost during migration (running both monolith and services)

⚠️ **Temporary architectural compromises**
- Shared database violates microservices principles initially
- Network hops add latency (in-process call becomes HTTP call)
- Fallback code adds complexity to Web BFF

⚠️ **Coordination overhead**
- Multiple slices require planning and sequencing
- Team must manage migration backlog alongside feature development
- Stakeholders may question "why not finished yet?" mid-migration

### Risks and Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| **Slice takes longer than estimated** | Delays subsequent slices | Build buffer into timeline, prioritize Slice 1 completion |
| **Performance regression** | User experience degrades | Baseline metrics before each slice, load testing, rollback if p95 > 2x |
| **Team fatigue** (too many slices) | Quality decreases, burnout | Limit to 1 slice at a time, celebrate completions, retrospectives |
| **Business pressure to rush** | Skip testing, introduce bugs | Educate stakeholders on incremental value, show early wins (Slice 1) |
| **Scope creep per slice** | Slices become too large | Strict acceptance criteria, ruthless scope control, ADR for changes |

## Validation Approach

**Per Slice**:
1. **Functional Testing**: Integration tests verify API contracts
2. **Load Testing**: K6 scripts validate performance (compare to baseline)
3. **Canary Deployment**: 10% → 50% → 100% traffic rollout with monitoring
4. **Observability**: Distributed tracing shows end-to-end request flow
5. **Rollback Drill**: Practice reverting to monolith (build muscle memory)

**Success Criteria (Per Slice)**:
- ✅ All acceptance criteria met (defined in Migration Plan)
- ✅ Error rate ≤ 1% (same as monolith)
- ✅ p95 latency ≤ baseline (or < 2x baseline if acceptable)
- ✅ No data loss or corruption
- ✅ Rollback tested and documented

**Post-Migration Validation**:
- ✅ Monolith decommissioned (all traffic routed to services)
- ✅ Shared database split (each service owns its data)
- ✅ Independent deployability demonstrated (deploy one service without others)
- ✅ Behavioral equivalence verified (A/B testing or shadow traffic)

## Alternative Patterns Considered

### 1. Anti-Corruption Layer (ACL)
**Description**: Create abstraction layer between monolith and services to translate concepts

**Pros**: Protects new services from monolith's domain model

**Cons**: Adds translation overhead, complex to maintain

**Decision**: Use ACL selectively (e.g., Checkout Service may translate monolith cart to internal model)

### 2. Event Interception
**Description**: Intercept monolith database writes, publish as events

**Pros**: Enables services to react to monolith changes without tight coupling

**Cons**: Requires Change Data Capture (CDC) infrastructure, complex tooling

**Decision**: Not needed initially (services call APIs directly), consider for Phase 2 (database split)

## Assumptions

1. **Kubernetes available**: Target deployment platform supports Kubernetes
2. **Team capacity**: 3-5 developers available for migration (not 100% dedicated, balanced with features)
3. **Stakeholder patience**: Business accepts 6-8 month timeline for complete migration
4. **Rollback acceptable**: Brief rollback (1-2 days) won't cause business harm
5. **Shared database acceptable temporarily**: DBA team allows multiple services connecting to same database

## Related Decisions

- **ADR-006**: API Gateway Selection - How traffic is routed during Strangler Fig
- **ADR-007**: Service Boundaries - Which bounded contexts become services
- **ADR-008**: Database Migration Strategy - Shared database vs. database-per-service
- **ADR-009**: Saga Pattern for Distributed Transactions - How Checkout Service coordinates

## References

- [Martin Fowler: Strangler Fig Application](https://martinfowler.com/bliki/StranglerFigApplication.html)
- [Sam Newman: Monolith to Microservices](https://www.oreilly.com/library/view/monolith-to-microservices/9781492047834/)
- [AWS: Strangler Fig Pattern](https://docs.aws.amazon.com/prescriptive-guidance/latest/modernization-decomposing-monoliths/strangler-fig.html)
- [Google Cloud: Migrating Monoliths](https://cloud.google.com/architecture/migrating-a-monolithic-app-to-microservices-gke)

## Review and Approval

**Reviewed by**: Architecture Team, Engineering Leads
**Approved by**: CTO, VP Engineering
**Date**: 2025-01-19

**Decision**: Proceed with Strangler Fig pattern, starting with Slice 0 (foundation) and Slice 1 (Product Catalog Service)
