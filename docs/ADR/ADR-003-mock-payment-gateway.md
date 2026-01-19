# ADR-003: Mock Payment Gateway for Development

## Status
Accepted (Development only)

## Context
The checkout flow requires integration with a payment processing system to charge customers. The application needs to:

- Support payment processing in the checkout workflow
- Enable development and testing without real payment credentials
- Avoid accidental charges during development
- Maintain flexibility to swap payment providers later
- Allow demonstration of the complete checkout flow

Production payment gateway integration (Stripe, PayPal, etc.) requires:
- API credentials and secrets management
- PCI compliance considerations
- Webhook handling for asynchronous payment confirmations
- Testing environments with separate API keys
- Cost per transaction (even in test mode for some providers)

For a demonstration/prototype application, these requirements add unnecessary complexity and risk.

## Decision
We will implement a **mock payment gateway** (`MockPaymentGateway`) that simulates payment processing without external dependencies.

### Implementation

**Interface**: `IPaymentGateway`
```csharp
public interface IPaymentGateway
{
    Task<PaymentResult> ChargeAsync(PaymentRequest req, CancellationToken ct = default);
}

public record PaymentRequest(decimal Amount, string Currency, string Token);
public record PaymentResult(bool Succeeded, string? ProviderRef, string? Error);
```

**Mock Implementation**: `MockPaymentGateway`
```csharp
public class MockPaymentGateway : IPaymentGateway
{
    public Task<PaymentResult> ChargeAsync(PaymentRequest req, CancellationToken ct = default)
    {
        // Always succeeds with mock reference
        return Task.FromResult(new PaymentResult(
            Succeeded: true, 
            ProviderRef: $"MOCK-{Guid.NewGuid():N}", 
            Error: null
        ));
    }
}
```

**Registration**: Scoped dependency in `Program.cs`
```csharp
builder.Services.AddScoped<IPaymentGateway, MockPaymentGateway>();
```

**Usage**: Injected into `CheckoutService`
```csharp
var pay = await _payments.ChargeAsync(new(total, "GBP", paymentToken), ct);
var status = pay.Succeeded ? "Paid" : "Failed";
```

### Design Characteristics

1. **Always Succeeds**: Returns `Succeeded = true` for all requests
2. **Mock Reference**: Generates unique GUID-based transaction reference
3. **No Validation**: Accepts any payment token without checking format or value
4. **Synchronous**: No network latency simulation (instant response)
5. **No State**: Stateless, doesn't track previous transactions

## Consequences

### Positive
- **Zero configuration**: No API keys, secrets, or external accounts needed
- **Fast development**: Instant payment responses, no network latency
- **Safe testing**: No risk of accidental charges or PCI compliance issues
- **Predictable**: Deterministic behavior enables reliable automated testing
- **Swappable**: Interface abstraction allows easy replacement with real gateway
- **No cost**: No transaction fees or provider charges during development

### Negative
- **Not production-ready**: Must be replaced before live deployment
- **No failure testing**: Can't easily test payment decline scenarios (comment suggests adding random fails)
- **Unrealistic timing**: Real gateways have network latency, webhooks, async confirmations
- **No validation**: Doesn't catch invalid token formats that real gateways would reject
- **No audit trail**: Doesn't log payment attempts for debugging

### Risks

1. **Accidental Production Use**
   - Risk: Mock gateway deployed to production
   - Current mitigation: None (no environment checks in code)
   - Recommended: Add environment validation or feature flag

2. **Incomplete Testing**
   - Risk: Payment failure paths not exercised
   - Current state: Code has `pay.Succeeded` check but never evaluates to false
   - Comment in code: `// trivial success for hack; add a random fail to demo error path if you like`

3. **Interface Mismatch**
   - Risk: Real gateway may require additional fields (billing address, CVV, 3D Secure)
   - Current mitigation: Simple `PaymentRequest` record keeps interface flexible

## Alternative Considered: Stripe Test Mode
**Rejected because**:
- Requires Stripe account creation and API key management
- Adds external dependency (network, Stripe availability)
- Introduces secrets management complexity
- Not necessary for demonstration purposes
- Would complicate local development setup

## Future Evolution Path

### Phase 1: Enhanced Mock (Recommended before production)
```csharp
public Task<PaymentResult> ChargeAsync(PaymentRequest req, CancellationToken ct)
{
    // Simulate random failures for testing
    if (Random.Shared.Next(100) < 10) // 10% failure rate
    {
        return Task.FromResult(new PaymentResult(false, null, "Card declined"));
    }
    
    // Validate token format
    if (!req.Token.StartsWith("tok_"))
    {
        return Task.FromResult(new PaymentResult(false, null, "Invalid token"));
    }
    
    return Task.FromResult(new PaymentResult(true, $"MOCK-{Guid.NewGuid():N}", null));
}
```

### Phase 2: Real Gateway Integration
- Implement `StripePaymentGateway : IPaymentGateway`
- Add configuration for API keys (Azure Key Vault or environment variables)
- Register based on environment: `builder.Environment.IsProduction() ? StripePaymentGateway : MockPaymentGateway`
- Add webhook handling for asynchronous payment confirmations
- Implement idempotency for retry safety

### Phase 3: Multiple Payment Methods
- Extend interface to support multiple payment types (card, wallet, bank transfer)
- Implement provider abstraction layer
- Add payment method selection in UI

## Operational Impact

**Current State**:
- All orders created in database show status "Paid"
- Every checkout succeeds (no declined payments in order history)
- Payment token field is populated but never validated ("tok_test" default)

**Demo Scenario**:
- Acceptable for demonstration and internal testing
- Shows happy path of checkout flow
- Allows focusing on application architecture rather than payment integration

**Production Readiness**: 
❌ Not production-ready
- Must replace before accepting real payments
- Consider adding startup validation to prevent accidental production use

## Related Decisions
- ADR-002: Monolithic architecture makes swapping implementations straightforward (DI container)
- Future: Event-driven payment processing (async webhook handling)
- Future: Saga pattern for compensating transactions if payment succeeds but order fails
