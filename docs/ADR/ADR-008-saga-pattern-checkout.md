# ADR-008: Saga Pattern for Distributed Checkout Transactions

## Status
Accepted

## Context

The Checkout flow in the current monolith is a multi-step process that involves:

1. **Retrieve cart** contents (Carts, CartLines tables)
2. **Reserve inventory** (decrement Inventory.Quantity)
3. **Process payment** (call external Payment Gateway)
4. **Create order** (insert Order and OrderLines)
5. **Clear cart** (delete CartLines)
6. **Publish events** (OrderCreated, PaymentProcessed)

**Current Implementation** (in monolith):
```csharp
public async Task<Order> CheckoutAsync(string customerId, string paymentToken)
{
    var cart = await _db.Carts.Include(c => c.Lines).SingleAsync(c => c.CustomerId == customerId);
    var total = cart.Lines.Sum(l => l.UnitPrice * l.Quantity);
    
    // Reserve inventory
    foreach (var line in cart.Lines)
    {
        var inv = await _db.Inventory.SingleAsync(i => i.Sku == line.Sku);
        inv.Quantity -= line.Quantity; // PROBLEM: What if payment fails after this?
    }
    
    // Process payment
    var pay = await _payments.ChargeAsync(new(total, "GBP", paymentToken));
    var status = pay.Succeeded ? "Paid" : "Failed";
    
    // Create order
    var order = new Order { Status = status, Total = total, Lines = [...] };
    _db.Orders.Add(order);
    
    // Clear cart
    _db.CartLines.RemoveRange(cart.Lines);
    
    await _db.SaveChangesAsync(); // Single database transaction
    return order;
}
```

**Problem**: In a monolithic architecture, this works because:
- ✅ Single database transaction (ACID guarantees)
- ✅ Rollback on failure (DbContext.SaveChangesAsync fails, entire transaction reverted)
- ✅ Immediate consistency

**Challenge in Microservices**:
Once services are separated:
- ❌ **No distributed transactions**: Cart Service, Payment Gateway, Order Service are separate systems
- ❌ **Partial failures**: Payment succeeds but Order Service is down → Customer charged, no order created
- ❌ **Compensation needed**: If payment fails after inventory reserved, must release inventory
- ❌ **Eventual consistency**: Services may temporarily be out of sync

**Key Questions**:
1. How to maintain consistency across multiple services without distributed transactions (2PC)?
2. How to handle failures mid-checkout (customer experience, data integrity)?
3. How to ensure idempotency (retry safety if network fails)?
4. How to make the system observable (track progress of checkout)?

## Decision

We will implement the **Saga Pattern (Orchestration-based)** for the Checkout Service to coordinate distributed transactions across services.

### Pattern Choice: Orchestration (not Choreography)

**Orchestration**: Central coordinator (Checkout Service) explicitly calls each service in sequence

```
              ┌──────────────────────┐
              │  Checkout Service    │
              │   (Orchestrator)     │
              └──────────┬───────────┘
                         │
        ┌────────────────┼────────────────┐
        │                │                │
        ▼                ▼                ▼
┌──────────────┐  ┌──────────────┐  ┌──────────────┐
│ Cart Service │  │   Payment    │  │Order Service │
│              │  │   Gateway    │  │              │
└──────────────┘  └──────────────┘  └──────────────┘
```

**Choreography**: Services react to events published by other services (no coordinator)

```
┌──────────────┐     OrderCreated      ┌──────────────┐
│ Cart Service ├───────────────────────►│Order Service │
└──────────────┘                        └──────────────┘
       │
       │ PaymentRequested
       ▼
┌──────────────┐    PaymentProcessed    ┌──────────────┐
│   Payment    ├───────────────────────►│Order Service │
│   Gateway    │                        └──────────────┘
└──────────────┘
```

**Decision**: Use **Orchestration** for Checkout

**Rationale**:
- ✅ **Explicit flow**: Checkout steps are sequential (can't create order before payment)
- ✅ **Easier to reason about**: Single place to understand checkout logic
- ✅ **Compensation logic centralized**: Orchestrator knows which steps succeeded, can compensate
- ✅ **Retry logic**: Orchestrator can retry failed steps (e.g., Order Service timeout)
- ✅ **Observability**: Single trace shows entire checkout flow

**Choreography Drawbacks** (for this use case):
- ❌ **Complex debugging**: Flow spread across multiple services (harder to trace failures)
- ❌ **Cyclic dependencies risk**: Service A publishes event → Service B reacts → Service A reacts (infinite loop)
- ❌ **No single source of truth**: Which service knows checkout status?

---

### Saga Implementation

**Checkout Saga Steps** (happy path):

```
┌────────────────────────────────────────────────────────────┐
│                      Checkout Saga                          │
├────────────────────────────────────────────────────────────┤
│  Step 1: Retrieve Cart                                     │
│    Action: GET /api/carts/{customerId}                     │
│    Compensate: N/A (read-only)                             │
├────────────────────────────────────────────────────────────┤
│  Step 2: Validate Products                                 │
│    Action: GET /api/products/{id} (for each item)          │
│    Compensate: N/A (read-only)                             │
├────────────────────────────────────────────────────────────┤
│  Step 3: Reserve Inventory                                 │
│    Action: POST /api/inventory/reserve                     │
│            { items: [{sku, qty}] }                         │
│    Compensate: POST /api/inventory/release                 │
│                { reservationId }                           │
├────────────────────────────────────────────────────────────┤
│  Step 4: Process Payment                                   │
│    Action: POST https://api.stripe.com/charges             │
│    Compensate: POST https://api.stripe.com/refunds         │
│                (future, requires payment integration)      │
├────────────────────────────────────────────────────────────┤
│  Step 5: Create Order                                      │
│    Action: POST /api/orders                                │
│            { customerId, items, total, paymentRef }        │
│    Compensate: POST /api/orders/{orderId}/cancel           │
│                (future)                                    │
├────────────────────────────────────────────────────────────┤
│  Step 6: Clear Cart                                        │
│    Action: DELETE /api/carts/{customerId}                  │
│    Compensate: POST /api/carts/{customerId}/restore        │
│                { cartSnapshot } (future)                   │
├────────────────────────────────────────────────────────────┤
│  Step 7: Publish OrderCreated Event                        │
│    Action: Publish to Azure Service Bus                    │
│    Compensate: Publish OrderCancelled event (future)       │
└────────────────────────────────────────────────────────────┘
```

**Failure Scenarios and Compensation**:

| Failure Point | Compensation Actions | Customer Experience |
|---------------|---------------------|---------------------|
| **Step 1 fails** (cart not found) | None needed | "Cart is empty" message |
| **Step 2 fails** (product inactive) | None needed | "Product no longer available" |
| **Step 3 fails** (insufficient inventory) | None needed | "Out of stock" message |
| **Step 4 fails** (payment declined) | Release inventory (Step 3 compensate) | "Payment failed, please try again" |
| **Step 5 fails** (Order Service down) | Refund payment (Step 4 compensate), Release inventory (Step 3 compensate) | "Error creating order, payment refunded" |
| **Step 6 fails** (Cart Service down) | Log warning (cart will auto-expire) | "Order placed, cart will clear shortly" |
| **Step 7 fails** (Service Bus down) | Retry event publishing (background job) | Order created successfully (analytics delayed) |

---

### Code Structure

**Checkout Service** (ASP.NET Core Web API):

```csharp
public class CheckoutOrchestrator : ICheckoutOrchestrator
{
    private readonly HttpClient _cartClient;
    private readonly HttpClient _inventoryClient;
    private readonly IPaymentGateway _paymentGateway;
    private readonly HttpClient _orderClient;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<CheckoutOrchestrator> _logger;

    public async Task<CheckoutResult> CheckoutAsync(string customerId, string paymentToken, CancellationToken ct)
    {
        string? reservationId = null;
        string? paymentRef = null;
        int? orderId = null;

        try
        {
            // Step 1: Retrieve cart
            _logger.LogInformation("Checkout started for customer {CustomerId}", customerId);
            var cart = await GetCartAsync(customerId, ct);
            if (cart.Lines.Count == 0)
                return CheckoutResult.Failure("Cart is empty");

            var total = cart.Lines.Sum(l => l.UnitPrice * l.Quantity);

            // Step 2: Validate products (optional, product IDs may have changed)
            // (skipped for brevity)

            // Step 3: Reserve inventory
            _logger.LogInformation("Reserving inventory for {ItemCount} items", cart.Lines.Count);
            var reservation = await ReserveInventoryAsync(cart.Lines, ct);
            if (!reservation.Success)
                return CheckoutResult.Failure($"Insufficient inventory: {reservation.Error}");
            reservationId = reservation.ReservationId;

            // Step 4: Process payment
            _logger.LogInformation("Processing payment of {Amount} {Currency}", total, "GBP");
            var payment = await _paymentGateway.ChargeAsync(new PaymentRequest(total, "GBP", paymentToken), ct);
            if (!payment.Succeeded)
            {
                // Compensate: Release inventory
                await ReleaseInventoryAsync(reservationId, ct);
                return CheckoutResult.Failure($"Payment failed: {payment.Error}");
            }
            paymentRef = payment.ProviderRef;

            // Step 5: Create order
            _logger.LogInformation("Creating order for customer {CustomerId}", customerId);
            var order = await CreateOrderAsync(customerId, cart, total, paymentRef, ct);
            orderId = order.Id;

            // Step 6: Clear cart (non-critical, log failure but don't roll back)
            try
            {
                await ClearCartAsync(customerId, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to clear cart for customer {CustomerId}, cart will expire", customerId);
            }

            // Step 7: Publish OrderCreated event (non-critical, retry in background)
            try
            {
                await _eventPublisher.PublishAsync(new OrderCreatedEvent
                {
                    OrderId = orderId.Value,
                    CustomerId = customerId,
                    Total = total,
                    Items = cart.Lines.Select(l => new OrderItemDto { Sku = l.Sku, Quantity = l.Quantity }).ToList()
                }, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to publish OrderCreated event for order {OrderId}", orderId);
                // TODO: Retry in background job (Hangfire, Azure Functions)
            }

            _logger.LogInformation("Checkout completed successfully for order {OrderId}", orderId);
            return CheckoutResult.Success(orderId.Value, total, paymentRef);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Checkout failed for customer {CustomerId}", customerId);

            // Compensate (reverse actions in reverse order)
            await CompensateAsync(reservationId, paymentRef, orderId, ct);

            return CheckoutResult.Failure("Checkout failed, please try again");
        }
    }

    private async Task CompensateAsync(string? reservationId, string? paymentRef, int? orderId, CancellationToken ct)
    {
        // Compensate Step 5: Cancel order (future)
        if (orderId.HasValue)
        {
            try
            {
                // await _orderClient.PostAsync($"/api/orders/{orderId}/cancel", null, ct);
                _logger.LogWarning("Order {OrderId} created but checkout failed, manual cancellation required", orderId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to compensate order {OrderId}", orderId);
            }
        }

        // Compensate Step 4: Refund payment (future, requires Stripe integration)
        if (paymentRef != null)
        {
            try
            {
                // await _paymentGateway.RefundAsync(paymentRef, ct);
                _logger.LogWarning("Payment {PaymentRef} succeeded but checkout failed, manual refund required", paymentRef);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to compensate payment {PaymentRef}", paymentRef);
            }
        }

        // Compensate Step 3: Release inventory
        if (reservationId != null)
        {
            try
            {
                await ReleaseInventoryAsync(reservationId, ct);
                _logger.LogInformation("Inventory reservation {ReservationId} released", reservationId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to release inventory reservation {ReservationId}", reservationId);
            }
        }
    }
}
```

---

### Idempotency

**Problem**: Network failures may cause retries → duplicate operations (e.g., charge customer twice)

**Solution**: Idempotency keys

**Implementation**:
```csharp
// Payment Gateway call with idempotency key
// Use combination of customer ID and cart hash for true idempotency
var cartHash = ComputeHash(cart.Lines); // Hash of cart contents
var idempotencyKey = $"checkout-{customerId}-{cartHash}";

var payment = await _paymentGateway.ChargeAsync(new PaymentRequest
{
    Amount = total,
    Currency = "GBP",
    Token = paymentToken,
    IdempotencyKey = idempotencyKey  // Same cart = same key, enables safe retries
}, ct);
```

**Result**: If request retried with same cart, Stripe returns cached response (no duplicate charge)

**Alternative**: Use GUID generated at saga start and persisted with saga state (reused on retries)

**Inventory Reservation Idempotency**:
```csharp
// POST /api/inventory/reserve
{
  "reservationId": "guid-from-orchestrator",  // Orchestrator generates, inventory service checks for duplicates
  "items": [{"sku": "WIDGET-001", "quantity": 2}]
}
```

---

### Observability

**Distributed Tracing** (OpenTelemetry):

Each step in saga creates span:
```
Trace: Checkout (500ms)
  ├─ Span: GET /api/carts/{customerId} (20ms)
  ├─ Span: POST /api/inventory/reserve (30ms)
  ├─ Span: POST https://api.stripe.com/charges (380ms)
  ├─ Span: POST /api/orders (40ms)
  ├─ Span: DELETE /api/carts/{customerId} (15ms)
  └─ Span: Publish OrderCreated (10ms)
```

**Correlation IDs**: Single ID propagates through all services (logs, traces, events)

**Saga State Logging**:
```json
{
  "timestamp": "2025-01-19T10:30:45Z",
  "level": "Information",
  "message": "Checkout saga step completed",
  "correlationId": "abc-123",
  "customerId": "guest",
  "step": "ReserveInventory",
  "status": "Success",
  "reservationId": "xyz-789"
}
```

---

### Saga State Machine (Future Enhancement)

**Problem**: Current implementation is procedural (linear steps in code)

**Enhancement**: Explicit state machine (using library like MassTransit or custom)

**States**:
```
┌──────────────┐
│   Started    │
└──────┬───────┘
       │
       ▼
┌──────────────┐
│ CartRetrieved│
└──────┬───────┘
       │
       ▼
┌──────────────┐
│InventoryRes. │
└──────┬───────┘
       │
       ├─────────────────┐
       │                 │ (if payment fails)
       ▼                 ▼
┌──────────────┐  ┌──────────────┐
│PaymentProcessed│  │ Compensating │
└──────┬───────┘  └──────────────┘
       │
       ▼
┌──────────────┐
│ OrderCreated │
└──────┬───────┘
       │
       ▼
┌──────────────┐
│  Completed   │
└──────────────┘
```

**Benefits**:
- Explicit state persistence (can resume saga after crash)
- Timeout handling (if step takes > 30s, compensate)
- Retry policies per step

**Library Options**:
- **MassTransit** (popular, supports RabbitMQ, Azure Service Bus)
- **NServiceBus** (enterprise, commercial license)
- **Custom** (using Hangfire or Azure Durable Functions)

**Decision**: Start with explicit orchestrator code (simpler), migrate to state machine if complexity grows

---

## Consequences

### Positive

✅ **Consistency without 2PC**
- Eventual consistency achieved via compensation
- No distributed transaction coordinator (XA, DTC) required

✅ **Resilience**
- Failures handled gracefully (compensating transactions)
- Partial failures don't leave system in inconsistent state

✅ **Observability**
- Single trace shows entire checkout flow
- Easy to identify failure point (which step failed?)

✅ **Autonomy**
- Services remain independent (no tight coupling)
- Can deploy Order Service without touching Checkout Service

✅ **Testability**
- Can test compensation logic (simulate failures in each step)
- Mock each service dependency in unit tests

### Negative

⚠️ **Complexity**
- More code than monolithic transaction (compensation logic)
- Harder to reason about (6-7 steps vs. single transaction)

⚠️ **Eventual consistency**
- Brief window where inventory reserved but order not yet created
- Reporting may show inconsistent state temporarily

⚠️ **Compensation not always possible**
- Payment refunds may take hours (Stripe refund not instant)
- Inventory reservation may timeout before compensation (stuck reservations)

⚠️ **Latency**
- Network hops add 50-100ms per step (vs. in-process calls)
- Total checkout time: 500ms (vs. 150ms in monolith)

⚠️ **Idempotency required**
- All service calls must be idempotent (more implementation work)
- Must generate and track idempotency keys

### Risks

| Risk | Mitigation |
|------|------------|
| **Compensation fails** (e.g., Inventory Service down) | Retry compensation in background job, manual intervention if needed |
| **Partial compensation** (refund succeeds, inventory release fails) | Monitor stuck reservations, auto-release after timeout |
| **Saga timeout** (step takes > 30s) | Timeout handling, compensate automatically |
| **Event loss** (Service Bus message dropped) | Durable queues, at-least-once delivery, deduplication in consumers |

---

## Alternatives Considered

### Alternative 1: Two-Phase Commit (2PC)

**Approach**: Distributed transaction coordinator (XA protocol, MS DTC)

**Pros**:
- ACID guarantees across services
- Familiar transaction model

**Cons**:
- ❌ **Performance**: Blocking protocol (locks held during prepare phase)
- ❌ **Availability**: Coordinator is single point of failure
- ❌ **Complexity**: Requires XA-compliant databases, transaction managers
- ❌ **Scalability**: Doesn't scale to cloud-native architectures

**Decision**: Rejected due to performance and availability concerns

---

### Alternative 2: Event Sourcing

**Approach**: Store events instead of state (append-only log)

**Pros**:
- Natural fit for saga pattern (events = saga steps)
- Full audit trail (every state change recorded)
- Replay events to reconstruct state

**Cons**:
- ❌ **Complexity**: Requires event store (EventStore, Apache Kafka)
- ❌ **Query complexity**: Reconstructing state requires replaying events
- ❌ **Schema evolution**: Changing event schema is hard

**Decision**: Rejected for initial implementation (too complex), consider for future

---

### Alternative 3: Choreography-Based Saga

**Approach**: Services publish events, other services react (no orchestrator)

**Example**:
```
Cart Service publishes: CheckoutRequested
  → Inventory Service reacts: InventoryReserved event
    → Payment Service reacts: PaymentProcessed event
      → Order Service reacts: OrderCreated event
```

**Pros**:
- No single point of failure (decentralized)
- Services fully decoupled (only know about events)

**Cons**:
- ❌ **Complex debugging**: Flow spread across services
- ❌ **No single view**: Which service knows overall checkout status?
- ❌ **Cyclic dependencies risk**: Service A reacts to B reacts to A (infinite loop)

**Decision**: Rejected for Checkout (better fit for choreography: notification workflows)

---

### Alternative 4: No Saga (Accept Inconsistency)

**Approach**: Best-effort, manual reconciliation if failures

**Pros**:
- Simplest implementation (no compensation logic)

**Cons**:
- ❌ **Customer impact**: Charged but no order → angry customers
- ❌ **Manual work**: Support team must manually refund, release inventory
- ❌ **Reputation risk**: Perceived as unreliable

**Decision**: Rejected (checkout is critical path, must be reliable)

---

## Implementation Plan

**Phase 1: Orchestrator in Monolith** (Slice 0-1)
- Extract CheckoutService logic into explicit saga pattern (within monolith)
- Add compensation logic (release inventory if payment fails)
- Test failure scenarios (mock payment failures)

**Phase 2: Distributed Saga** (Slice 3)
- Deploy Checkout Service as separate microservice
- Call Cart Service, Inventory Service, Order Service via HTTP
- Implement idempotency keys
- Add distributed tracing

**Phase 3: Event Publishing** (Slice 3)
- Integrate Azure Service Bus
- Publish OrderCreated, PaymentProcessed events
- Retry failed event publishing in background job

**Phase 4: State Machine** (Future)
- If saga complexity grows, migrate to MassTransit or Durable Functions
- Persist saga state (support resume after crash)

---

## Testing Strategy

**Unit Tests**:
- Test each saga step in isolation (mock dependencies)
- Test compensation logic (simulate failures)

**Integration Tests**:
- Test happy path (all steps succeed)
- Test failure scenarios (each step fails, verify compensation)

**Chaos Testing**:
- Kill services mid-checkout (Checkout Service crashes after payment)
- Network delays (simulate 30s timeout)
- Event loss (Service Bus unavailable)

**Load Testing**:
- 100 concurrent checkouts
- Verify no race conditions (inventory overselling)

---

## Related Decisions

- **ADR-005**: Strangler Fig Pattern - Checkout Service extracted incrementally
- **ADR-006**: Kubernetes Deployment - Checkout Service runs as container
- **ADR-007**: Service Boundaries - Checkout Service orchestrates Cart, Payment, Order
- **ADR-009**: Event-Driven Architecture - Checkout publishes OrderCreated event

## References

- [Saga Pattern (Chris Richardson)](https://microservices.io/patterns/data/saga.html)
- [Designing Data-Intensive Applications (Martin Kleppmann)](https://dataintensive.net/) - Chapter 9: Consistency and Consensus
- [Enterprise Integration Patterns (Gregor Hohpe)](https://www.enterpriseintegrationpatterns.com/patterns/messaging/ProcessManager.html)
- [MassTransit Sagas](https://masstransit-project.com/usage/sagas/)

## Review and Approval

**Reviewed by**: Architecture Team, Checkout Team Lead
**Approved by**: CTO, VP Engineering
**Date**: 2025-01-19

**Decision**: Implement Orchestration-based Saga for Checkout Service
