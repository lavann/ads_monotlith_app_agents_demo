using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RetailMonolith.Data;
using RetailMonolith.Models;
using RetailMonolith.Services;

namespace RetailMonolith.UnitTests.Services;

public class CartServiceTests : IDisposable
{
    private readonly AppDbContext _dbContext;
    private readonly CartService _cartService;

    public CartServiceTests()
    {
        // Create in-memory database for testing
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _dbContext = new AppDbContext(options);
        _cartService = new CartService(_dbContext);
    }

    public void Dispose()
    {
        _dbContext.Database.EnsureDeleted();
        _dbContext.Dispose();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task AddToCartAsync_WithNewProduct_AddsNewCartLine()
    {
        // Arrange
        const string customerId = "test-customer";
        var product = new Product
        {
            Id = 1,
            Sku = "SKU-001",
            Name = "Test Product",
            Price = 29.99m,
            Currency = "GBP",
            IsActive = true
        };
        await _dbContext.Products.AddAsync(product);
        await _dbContext.SaveChangesAsync();

        // Act
        await _cartService.AddToCartAsync(customerId, product.Id, quantity: 2);

        // Assert
        var cart = await _dbContext.Carts
            .Include(c => c.Lines)
            .FirstOrDefaultAsync(c => c.CustomerId == customerId);

        cart.Should().NotBeNull();
        cart!.Lines.Should().HaveCount(1);
        cart.Lines[0].Sku.Should().Be("SKU-001");
        cart.Lines[0].Name.Should().Be("Test Product");
        cart.Lines[0].UnitPrice.Should().Be(29.99m);
        cart.Lines[0].Quantity.Should().Be(2);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task AddToCartAsync_WithExistingProduct_IncrementsQuantity()
    {
        // Arrange
        const string customerId = "test-customer";
        var product = new Product
        {
            Id = 1,
            Sku = "SKU-001",
            Name = "Test Product",
            Price = 29.99m,
            Currency = "GBP",
            IsActive = true
        };
        await _dbContext.Products.AddAsync(product);
        await _dbContext.SaveChangesAsync();

        // Add product first time
        await _cartService.AddToCartAsync(customerId, product.Id, quantity: 2);

        // Act - Add same product again
        await _cartService.AddToCartAsync(customerId, product.Id, quantity: 3);

        // Assert
        var cart = await _dbContext.Carts
            .Include(c => c.Lines)
            .FirstOrDefaultAsync(c => c.CustomerId == customerId);

        cart.Should().NotBeNull();
        cart!.Lines.Should().HaveCount(1, "same product should increment, not add new line");
        cart.Lines[0].Quantity.Should().Be(5, "2 + 3 should equal 5");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task AddToCartAsync_WithInvalidProductId_ThrowsException()
    {
        // Arrange
        const string customerId = "test-customer";
        const int invalidProductId = 999;

        // Act
        Func<Task> act = async () => await _cartService.AddToCartAsync(customerId, invalidProductId, quantity: 1);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Invalid product ID");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task AddToCartAsync_WithMultipleDifferentProducts_AddsMultipleLines()
    {
        // Arrange
        const string customerId = "test-customer";
        var product1 = new Product { Id = 1, Sku = "SKU-001", Name = "Product 1", Price = 10m, Currency = "GBP", IsActive = true };
        var product2 = new Product { Id = 2, Sku = "SKU-002", Name = "Product 2", Price = 20m, Currency = "GBP", IsActive = true };
        await _dbContext.Products.AddRangeAsync(product1, product2);
        await _dbContext.SaveChangesAsync();

        // Act
        await _cartService.AddToCartAsync(customerId, product1.Id, quantity: 1);
        await _cartService.AddToCartAsync(customerId, product2.Id, quantity: 2);

        // Assert
        var cart = await _dbContext.Carts
            .Include(c => c.Lines)
            .FirstOrDefaultAsync(c => c.CustomerId == customerId);

        cart.Should().NotBeNull();
        cart!.Lines.Should().HaveCount(2);
        cart.Lines.Should().Contain(l => l.Sku == "SKU-001" && l.Quantity == 1);
        cart.Lines.Should().Contain(l => l.Sku == "SKU-002" && l.Quantity == 2);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetOrCreateCartAsync_WithExistingCart_ReturnsCart()
    {
        // Arrange
        const string customerId = "test-customer";
        var existingCart = new Cart { CustomerId = customerId };
        await _dbContext.Carts.AddAsync(existingCart);
        await _dbContext.SaveChangesAsync();

        // Act
        var cart = await _cartService.GetOrCreateCartAsync(customerId);

        // Assert
        cart.Should().NotBeNull();
        cart.CustomerId.Should().Be(customerId);
        cart.Id.Should().Be(existingCart.Id);
        cart.Lines.Should().NotBeNull();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetOrCreateCartAsync_WithNewCustomer_CreatesCart()
    {
        // Arrange
        const string customerId = "new-customer";

        // Act
        var cart = await _cartService.GetOrCreateCartAsync(customerId);

        // Assert
        cart.Should().NotBeNull();
        cart.CustomerId.Should().Be(customerId);
        cart.Id.Should().BeGreaterThan(0, "cart should be persisted with an ID");

        // Verify cart is in database
        var dbCart = await _dbContext.Carts.FirstOrDefaultAsync(c => c.CustomerId == customerId);
        dbCart.Should().NotBeNull();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetOrCreateCartAsync_CalledMultipleTimes_ReturnsTheSameCart()
    {
        // Arrange
        const string customerId = "test-customer";

        // Act
        var cart1 = await _cartService.GetOrCreateCartAsync(customerId);
        var cart2 = await _cartService.GetOrCreateCartAsync(customerId);

        // Assert
        cart1.Id.Should().Be(cart2.Id, "should return the same cart, not create a new one");
        var cartCount = await _dbContext.Carts.CountAsync(c => c.CustomerId == customerId);
        cartCount.Should().Be(1, "should only have one cart in database");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetCartWithLinesAsync_WithExistingCartAndLines_ReturnsCartWithLines()
    {
        // Arrange
        const string customerId = "test-customer";
        var cart = new Cart { CustomerId = customerId };
        cart.Lines.Add(new CartLine
        {
            Sku = "SKU-001",
            Name = "Product 1",
            UnitPrice = 10m,
            Quantity = 2
        });
        await _dbContext.Carts.AddAsync(cart);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _cartService.GetCartWithLinesAsync(customerId);

        // Assert
        result.Should().NotBeNull();
        result.CustomerId.Should().Be(customerId);
        result.Lines.Should().HaveCount(1);
        result.Lines[0].Sku.Should().Be("SKU-001");
        result.Lines[0].Quantity.Should().Be(2);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetCartWithLinesAsync_WithNoCart_ReturnsEmptyCart()
    {
        // Arrange
        const string customerId = "non-existent-customer";

        // Act
        var result = await _cartService.GetCartWithLinesAsync(customerId);

        // Assert
        result.Should().NotBeNull();
        result.CustomerId.Should().Be(customerId);
        result.Id.Should().Be(0, "new cart instance should have default ID");
        result.Lines.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ClearCartAsync_WithExistingCart_RemovesCart()
    {
        // Arrange
        const string customerId = "test-customer";
        var cart = new Cart { CustomerId = customerId };
        cart.Lines.Add(new CartLine
        {
            Sku = "SKU-001",
            Name = "Product 1",
            UnitPrice = 10m,
            Quantity = 2
        });
        await _dbContext.Carts.AddAsync(cart);
        await _dbContext.SaveChangesAsync();

        // Act
        await _cartService.ClearCartAsync(customerId);

        // Assert
        var dbCart = await _dbContext.Carts
            .Include(c => c.Lines)
            .FirstOrDefaultAsync(c => c.CustomerId == customerId);
        dbCart.Should().BeNull("cart should be removed from database");

        var lineCount = await _dbContext.CartLines.CountAsync();
        lineCount.Should().Be(0, "all cart lines should also be removed");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ClearCartAsync_WithNoCart_DoesNotThrowException()
    {
        // Arrange
        const string customerId = "non-existent-customer";

        // Act
        Func<Task> act = async () => await _cartService.ClearCartAsync(customerId);

        // Assert
        await act.Should().NotThrowAsync("should handle non-existent cart gracefully");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task AddToCartAsync_WithDefaultQuantity_AddsOneItem()
    {
        // Arrange
        const string customerId = "test-customer";
        var product = new Product
        {
            Id = 1,
            Sku = "SKU-001",
            Name = "Test Product",
            Price = 29.99m,
            Currency = "GBP",
            IsActive = true
        };
        await _dbContext.Products.AddAsync(product);
        await _dbContext.SaveChangesAsync();

        // Act - Don't specify quantity (should default to 1)
        await _cartService.AddToCartAsync(customerId, product.Id);

        // Assert
        var cart = await _dbContext.Carts
            .Include(c => c.Lines)
            .FirstOrDefaultAsync(c => c.CustomerId == customerId);

        cart.Should().NotBeNull();
        cart!.Lines[0].Quantity.Should().Be(1, "default quantity should be 1");
    }
}
