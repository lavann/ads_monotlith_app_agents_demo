using FluentAssertions;
using RetailMonolith.Services;

namespace RetailMonolith.UnitTests.Services;

public class MockPaymentGatewayTests
{
    private readonly MockPaymentGateway _gateway;

    public MockPaymentGatewayTests()
    {
        _gateway = new MockPaymentGateway();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ChargeAsync_ReturnsSuccessfulResult()
    {
        // Arrange
        var request = new PaymentRequest(100m, "GBP", "test-token");

        // Act
        var result = await _gateway.ChargeAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ChargeAsync_ReturnsProviderReference()
    {
        // Arrange
        var request = new PaymentRequest(100m, "GBP", "test-token");

        // Act
        var result = await _gateway.ChargeAsync(request);

        // Assert
        result.ProviderRef.Should().NotBeNullOrEmpty();
        result.ProviderRef.Should().StartWith("MOCK-", "provider reference should have MOCK- prefix");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ChargeAsync_WithAnyAmount_Succeeds()
    {
        // Arrange
        var testAmounts = new[] { 0m, 0.01m, 1m, 100m, 999999.99m };

        foreach (var amount in testAmounts)
        {
            var request = new PaymentRequest(amount, "GBP", "test-token");

            // Act
            var result = await _gateway.ChargeAsync(request);

            // Assert
            result.Succeeded.Should().BeTrue($"payment with amount {amount} should succeed");
        }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ChargeAsync_WithAnyCurrency_Succeeds()
    {
        // Arrange
        var testCurrencies = new[] { "GBP", "USD", "EUR", "JPY", "INVALID" };

        foreach (var currency in testCurrencies)
        {
            var request = new PaymentRequest(100m, currency, "test-token");

            // Act
            var result = await _gateway.ChargeAsync(request);

            // Assert
            result.Succeeded.Should().BeTrue($"payment with currency {currency} should succeed");
        }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ChargeAsync_WithAnyToken_Succeeds()
    {
        // Arrange
        var testTokens = new[] { "valid-token", "", "invalid", "null", "12345" };

        foreach (var token in testTokens)
        {
            var request = new PaymentRequest(100m, "GBP", token);

            // Act
            var result = await _gateway.ChargeAsync(request);

            // Assert
            result.Succeeded.Should().BeTrue($"payment with token '{token}' should succeed");
        }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ChargeAsync_ReturnsNoError()
    {
        // Arrange
        var request = new PaymentRequest(100m, "GBP", "test-token");

        // Act
        var result = await _gateway.ChargeAsync(request);

        // Assert
        result.Error.Should().BeNull("successful payment should have no error");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ChargeAsync_GeneratesUniqueReferences()
    {
        // Arrange
        var request = new PaymentRequest(100m, "GBP", "test-token");
        var references = new HashSet<string>();

        // Act - Make multiple calls
        for (int i = 0; i < 100; i++)
        {
            var result = await _gateway.ChargeAsync(request);
            references.Add(result.ProviderRef!);
        }

        // Assert
        references.Should().HaveCount(100, "each call should generate a unique reference");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ChargeAsync_ProviderReferenceHasValidGuidFormat()
    {
        // Arrange
        var request = new PaymentRequest(100m, "GBP", "test-token");

        // Act
        var result = await _gateway.ChargeAsync(request);

        // Assert
        var refWithoutPrefix = result.ProviderRef!.Replace("MOCK-", "");
        Guid.TryParse(refWithoutPrefix, out _).Should().BeTrue("reference should contain a valid GUID");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ChargeAsync_CompletesQuickly()
    {
        // Arrange
        var request = new PaymentRequest(100m, "GBP", "test-token");
        var startTime = DateTime.UtcNow;

        // Act
        await _gateway.ChargeAsync(request);

        // Assert
        var duration = DateTime.UtcNow - startTime;
        duration.Should().BeLessThan(TimeSpan.FromMilliseconds(100), "mock gateway should be fast");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ChargeAsync_WithCancellationToken_DoesNotThrow()
    {
        // Arrange
        var request = new PaymentRequest(100m, "GBP", "test-token");
        var cts = new CancellationTokenSource();

        // Act
        Func<Task> act = async () => await _gateway.ChargeAsync(request, cts.Token);

        // Assert
        await act.Should().NotThrowAsync("mock gateway should handle cancellation tokens");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ChargeAsync_MultipleSequentialCalls_AllSucceed()
    {
        // Arrange
        var requests = new[]
        {
            new PaymentRequest(10m, "GBP", "token1"),
            new PaymentRequest(20m, "USD", "token2"),
            new PaymentRequest(30m, "EUR", "token3")
        };

        // Act & Assert
        foreach (var request in requests)
        {
            var result = await _gateway.ChargeAsync(request);
            result.Succeeded.Should().BeTrue();
            result.ProviderRef.Should().NotBeNullOrEmpty();
        }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ChargeAsync_WithNegativeAmount_StillSucceeds()
    {
        // Arrange
        var request = new PaymentRequest(-100m, "GBP", "test-token");

        // Act
        var result = await _gateway.ChargeAsync(request);

        // Assert
        result.Succeeded.Should().BeTrue("mock gateway has no validation, should succeed even with negative amount");
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData(0.01)]
    [InlineData(1.00)]
    [InlineData(99.99)]
    [InlineData(1000.00)]
    [InlineData(9999.99)]
    public async Task ChargeAsync_WithVariousAmounts_AlwaysSucceeds(decimal amount)
    {
        // Arrange
        var request = new PaymentRequest(amount, "GBP", "test-token");

        // Act
        var result = await _gateway.ChargeAsync(request);

        // Assert
        result.Succeeded.Should().BeTrue();
        result.ProviderRef.Should().NotBeNullOrEmpty();
        result.Error.Should().BeNull();
    }
}
