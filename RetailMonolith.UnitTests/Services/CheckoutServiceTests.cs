using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using RetailMonolith.Data;
using RetailMonolith.Models;
using RetailMonolith.Services;

namespace RetailMonolith.UnitTests.Services;

public class CheckoutServiceTests : IDisposable
{
    private readonly AppDbContext _dbContext;
    private readonly Mock<IPaymentGateway> _mockPaymentGateway;
    private readonly CheckoutService _checkoutService;

    public CheckoutServiceTests()
    {
        // Create in-memory database for testing
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _dbContext = new AppDbContext(options);
        _mockPaymentGateway = new Mock<IPaymentGateway>();
        _checkoutService = new CheckoutService(_dbContext, _mockPaymentGateway.Object);
    }

    public void Dispose()
    {
        _dbContext.Database.EnsureDeleted();
        _dbContext.Dispose();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CheckoutAsync_WithValidCart_CreatesOrder()
    {
        // Arrange
        const string customerId = "test-customer";
        var cart = await CreateCartWithProductsAsync(customerId);
        
        _mockPaymentGateway
            .Setup(x => x.ChargeAsync(It.IsAny<PaymentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentResult(true, "MOCK-12345", null));

        // Act
        var order = await _checkoutService.CheckoutAsync(customerId, "test-token");

        // Assert
        order.Should().NotBeNull();
        order.Id.Should().BeGreaterThan(0, "order should be persisted");
        order.CustomerId.Should().Be(customerId);
        order.Lines.Should().HaveCount(2);
        order.Total.Should().Be(70m); // (10 * 2) + (25 * 2)
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CheckoutAsync_WithEmptyCart_ThrowsException()
    {
        // Arrange
        const string customerId = "customer-with-no-cart";

        // Act
        Func<Task> act = async () => await _checkoutService.CheckoutAsync(customerId, "test-token");

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Cart not found");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CheckoutAsync_WithInsufficientInventory_ThrowsException()
    {
        // Arrange
        const string customerId = "test-customer";
        var cart = new Cart { CustomerId = customerId };
        cart.Lines.Add(new CartLine
        {
            Sku = "SKU-001",
            Name = "Low Stock Product",
            UnitPrice = 10m,
            Quantity = 100 // More than available
        });
        await _dbContext.Carts.AddAsync(cart);
        
        // Add inventory with insufficient quantity
        await _dbContext.Inventory.AddAsync(new InventoryItem
        {
            Sku = "SKU-001",
            Quantity = 50 // Less than requested
        });
        await _dbContext.SaveChangesAsync();

        // Act
        Func<Task> act = async () => await _checkoutService.CheckoutAsync(customerId, "test-token");

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Out of stock: SKU-001");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CheckoutAsync_WithSuccessfulPayment_SetsOrderStatusToPaid()
    {
        // Arrange
        const string customerId = "test-customer";
        await CreateCartWithProductsAsync(customerId);
        
        _mockPaymentGateway
            .Setup(x => x.ChargeAsync(It.IsAny<PaymentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentResult(true, "MOCK-SUCCESS", null));

        // Act
        var order = await _checkoutService.CheckoutAsync(customerId, "test-token");

        // Assert
        order.Status.Should().Be("Paid");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CheckoutAsync_WithFailedPayment_SetsOrderStatusToFailed()
    {
        // Arrange
        const string customerId = "test-customer";
        await CreateCartWithProductsAsync(customerId);
        
        _mockPaymentGateway
            .Setup(x => x.ChargeAsync(It.IsAny<PaymentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentResult(false, null, "Payment declined"));

        // Act
        var order = await _checkoutService.CheckoutAsync(customerId, "test-token");

        // Assert
        order.Status.Should().Be("Failed");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CheckoutAsync_DecrementsInventory()
    {
        // Arrange
        const string customerId = "test-customer";
        var cart = new Cart { CustomerId = customerId };
        cart.Lines.Add(new CartLine
        {
            Sku = "SKU-001",
            Name = "Product 1",
            UnitPrice = 10m,
            Quantity = 3
        });
        await _dbContext.Carts.AddAsync(cart);
        
        var inventory = new InventoryItem { Sku = "SKU-001", Quantity = 100 };
        await _dbContext.Inventory.AddAsync(inventory);
        await _dbContext.SaveChangesAsync();
        
        _mockPaymentGateway
            .Setup(x => x.ChargeAsync(It.IsAny<PaymentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentResult(true, "MOCK-12345", null));

        // Act
        await _checkoutService.CheckoutAsync(customerId, "test-token");

        // Assert
        var updatedInventory = await _dbContext.Inventory.FirstAsync(i => i.Sku == "SKU-001");
        updatedInventory.Quantity.Should().Be(97, "100 - 3 = 97");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CheckoutAsync_ClearsCartLines()
    {
        // Arrange
        const string customerId = "test-customer";
        await CreateCartWithProductsAsync(customerId);
        
        _mockPaymentGateway
            .Setup(x => x.ChargeAsync(It.IsAny<PaymentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentResult(true, "MOCK-12345", null));

        // Act
        await _checkoutService.CheckoutAsync(customerId, "test-token");

        // Assert
        var cartLines = await _dbContext.CartLines.ToListAsync();
        cartLines.Should().BeEmpty("cart lines should be removed after checkout");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CheckoutAsync_CalculatesTotalCorrectly()
    {
        // Arrange
        const string customerId = "test-customer";
        var cart = new Cart { CustomerId = customerId };
        cart.Lines.Add(new CartLine { Sku = "SKU-001", Name = "Product 1", UnitPrice = 10.50m, Quantity = 3 });
        cart.Lines.Add(new CartLine { Sku = "SKU-002", Name = "Product 2", UnitPrice = 25.99m, Quantity = 2 });
        cart.Lines.Add(new CartLine { Sku = "SKU-003", Name = "Product 3", UnitPrice = 5.00m, Quantity = 1 });
        await _dbContext.Carts.AddAsync(cart);
        
        await _dbContext.Inventory.AddRangeAsync(
            new InventoryItem { Sku = "SKU-001", Quantity = 100 },
            new InventoryItem { Sku = "SKU-002", Quantity = 100 },
            new InventoryItem { Sku = "SKU-003", Quantity = 100 }
        );
        await _dbContext.SaveChangesAsync();
        
        _mockPaymentGateway
            .Setup(x => x.ChargeAsync(It.IsAny<PaymentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentResult(true, "MOCK-12345", null));

        // Act
        var order = await _checkoutService.CheckoutAsync(customerId, "test-token");

        // Assert
        var expectedTotal = (10.50m * 3) + (25.99m * 2) + (5.00m * 1);
        order.Total.Should().Be(expectedTotal);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CheckoutAsync_CallsPaymentGatewayWithCorrectAmount()
    {
        // Arrange
        const string customerId = "test-customer";
        await CreateCartWithProductsAsync(customerId);
        
        _mockPaymentGateway
            .Setup(x => x.ChargeAsync(It.IsAny<PaymentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentResult(true, "MOCK-12345", null));

        // Act
        await _checkoutService.CheckoutAsync(customerId, "test-token");

        // Assert
        _mockPaymentGateway.Verify(
            x => x.ChargeAsync(
                It.Is<PaymentRequest>(req => req.Amount == 70m && req.Currency == "GBP" && req.Token == "test-token"),
                It.IsAny<CancellationToken>()
            ),
            Times.Once
        );
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CheckoutAsync_CreatesOrderLinesMatchingCartLines()
    {
        // Arrange
        const string customerId = "test-customer";
        await CreateCartWithProductsAsync(customerId);
        
        _mockPaymentGateway
            .Setup(x => x.ChargeAsync(It.IsAny<PaymentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentResult(true, "MOCK-12345", null));

        // Act
        var order = await _checkoutService.CheckoutAsync(customerId, "test-token");

        // Assert
        order.Lines.Should().HaveCount(2);
        order.Lines.Should().Contain(l => l.Sku == "SKU-001" && l.Name == "Product 1" && l.UnitPrice == 10m && l.Quantity == 2);
        order.Lines.Should().Contain(l => l.Sku == "SKU-002" && l.Name == "Product 2" && l.UnitPrice == 25m && l.Quantity == 2);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CheckoutAsync_SetsOrderTimestamp()
    {
        // Arrange
        const string customerId = "test-customer";
        await CreateCartWithProductsAsync(customerId);
        
        _mockPaymentGateway
            .Setup(x => x.ChargeAsync(It.IsAny<PaymentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentResult(true, "MOCK-12345", null));

        var beforeCheckout = DateTime.UtcNow;

        // Act
        var order = await _checkoutService.CheckoutAsync(customerId, "test-token");

        // Assert
        var afterCheckout = DateTime.UtcNow;
        order.CreatedUtc.Should().BeOnOrAfter(beforeCheckout);
        order.CreatedUtc.Should().BeOnOrBefore(afterCheckout);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CheckoutAsync_WithMultipleProducts_DecrementsAllInventory()
    {
        // Arrange
        const string customerId = "test-customer";
        var cart = new Cart { CustomerId = customerId };
        cart.Lines.Add(new CartLine { Sku = "SKU-001", Name = "Product 1", UnitPrice = 10m, Quantity = 3 });
        cart.Lines.Add(new CartLine { Sku = "SKU-002", Name = "Product 2", UnitPrice = 20m, Quantity = 5 });
        await _dbContext.Carts.AddAsync(cart);
        
        await _dbContext.Inventory.AddRangeAsync(
            new InventoryItem { Sku = "SKU-001", Quantity = 100 },
            new InventoryItem { Sku = "SKU-002", Quantity = 50 }
        );
        await _dbContext.SaveChangesAsync();
        
        _mockPaymentGateway
            .Setup(x => x.ChargeAsync(It.IsAny<PaymentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentResult(true, "MOCK-12345", null));

        // Act
        await _checkoutService.CheckoutAsync(customerId, "test-token");

        // Assert
        var inventory1 = await _dbContext.Inventory.FirstAsync(i => i.Sku == "SKU-001");
        var inventory2 = await _dbContext.Inventory.FirstAsync(i => i.Sku == "SKU-002");
        
        inventory1.Quantity.Should().Be(97, "100 - 3 = 97");
        inventory2.Quantity.Should().Be(45, "50 - 5 = 45");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CheckoutAsync_PersistsOrderToDatabase()
    {
        // Arrange
        const string customerId = "test-customer";
        await CreateCartWithProductsAsync(customerId);
        
        _mockPaymentGateway
            .Setup(x => x.ChargeAsync(It.IsAny<PaymentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentResult(true, "MOCK-12345", null));

        // Act
        var order = await _checkoutService.CheckoutAsync(customerId, "test-token");

        // Assert
        var dbOrder = await _dbContext.Orders
            .Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == order.Id);
        
        dbOrder.Should().NotBeNull();
        dbOrder!.CustomerId.Should().Be(customerId);
        dbOrder.Lines.Should().HaveCount(2);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CheckoutAsync_WithZeroQuantity_StillProcessesOrder()
    {
        // Arrange
        const string customerId = "test-customer";
        var cart = new Cart { CustomerId = customerId };
        cart.Lines.Add(new CartLine { Sku = "SKU-001", Name = "Product 1", UnitPrice = 10m, Quantity = 0 });
        await _dbContext.Carts.AddAsync(cart);
        
        await _dbContext.Inventory.AddAsync(new InventoryItem { Sku = "SKU-001", Quantity = 100 });
        await _dbContext.SaveChangesAsync();
        
        _mockPaymentGateway
            .Setup(x => x.ChargeAsync(It.IsAny<PaymentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentResult(true, "MOCK-12345", null));

        // Act
        var order = await _checkoutService.CheckoutAsync(customerId, "test-token");

        // Assert
        order.Should().NotBeNull();
        order.Total.Should().Be(0m);
    }

    /// <summary>
    /// Helper method to create a cart with test products and inventory
    /// </summary>
    private async Task<Cart> CreateCartWithProductsAsync(string customerId)
    {
        var cart = new Cart { CustomerId = customerId };
        cart.Lines.Add(new CartLine
        {
            Sku = "SKU-001",
            Name = "Product 1",
            UnitPrice = 10m,
            Quantity = 2
        });
        cart.Lines.Add(new CartLine
        {
            Sku = "SKU-002",
            Name = "Product 2",
            UnitPrice = 25m,
            Quantity = 2
        });
        
        await _dbContext.Carts.AddAsync(cart);
        await _dbContext.Inventory.AddRangeAsync(
            new InventoryItem { Sku = "SKU-001", Quantity = 100 },
            new InventoryItem { Sku = "SKU-002", Quantity = 100 }
        );
        await _dbContext.SaveChangesAsync();
        
        return cart;
    }
}
