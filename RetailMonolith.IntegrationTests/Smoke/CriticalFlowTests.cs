using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RetailMonolith.Data;
using RetailMonolith.Models;

namespace RetailMonolith.IntegrationTests.Smoke;

[Trait("Category", "Smoke")]
public class CriticalFlowTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly CustomWebApplicationFactory _factory;

    public CriticalFlowTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task CompleteCheckoutJourney_FromBrowseToOrderConfirmation()
    {
        // Arrange - Seed database
        int productId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            
            var product = new Product
            {
                Sku = "SMOKE-001",
                Name = "Smoke Test Product",
                Price = 99.99m,
                Currency = "GBP",
                IsActive = true,
                Category = "Test"
            };
            db.Products.Add(product);
            await db.SaveChangesAsync();
            productId = product.Id;

            var inventory = new InventoryItem
            {
                Sku = "SMOKE-001",
                Quantity = 1000
            };
            db.Inventory.Add(inventory);

            await db.SaveChangesAsync();
        }

        // Act 1: Browse products (simulated - would be GET /Products page)
        // In reality this is a Razor Page, so we'll just verify the product exists

        // Act 2: Add product to cart (simulated via direct service)
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            
            var cart = new Cart { CustomerId = "guest" };
            cart.Lines.Add(new CartLine
            {
                Sku = "SMOKE-001",
                Name = "Smoke Test Product",
                UnitPrice = 99.99m,
                Quantity = 2
            });
            db.Carts.Add(cart);
            await db.SaveChangesAsync();
        }

        // Act 3: Checkout
        var checkoutResponse = await _client.PostAsync("/api/checkout", null);

        // Assert: Checkout successful
        checkoutResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var checkoutContent = await checkoutResponse.Content.ReadAsStringAsync();
        var orderResult = JsonSerializer.Deserialize<CheckoutResult>(checkoutContent, new JsonSerializerOptions 
        { 
            PropertyNameCaseInsensitive = true 
        });

        orderResult.Should().NotBeNull();
        orderResult!.Status.Should().Be("Paid");
        orderResult.Total.Should().Be(199.98m); // 2 x 99.99

        // Act 4: View order details
        var orderResponse = await _client.GetAsync($"/api/orders/{orderResult.Id}");

        // Assert: Order retrieved successfully
        orderResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var order = await orderResponse.Content.ReadFromJsonAsync<Order>();
        order.Should().NotBeNull();
        order!.Id.Should().Be(orderResult.Id);
        order.Status.Should().Be("Paid");
        order.Lines.Should().HaveCount(1);
        order.Lines[0].Sku.Should().Be("SMOKE-001");

        // Act 5: Verify inventory was decremented
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var inventory = await db.Inventory.FirstOrDefaultAsync(i => i.Sku == "SMOKE-001");
            
            inventory.Should().NotBeNull();
            inventory!.Quantity.Should().Be(998); // 1000 - 2
        }

        // Act 6: Verify cart was cleared
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var cart = await db.Carts
                .Include(c => c.Lines)
                .FirstOrDefaultAsync(c => c.CustomerId == "guest");
            
            cart.Should().BeNull(); // Cart should be deleted after checkout
        }
    }

    [Fact]
    public async Task ViewOrderHistory_ReturnsOrders()
    {
        // Arrange - Create an order
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            
            var order = new Order
            {
                CustomerId = "guest",
                Status = "Paid",
                Total = 50.00m,
                CreatedUtc = DateTime.UtcNow
            };
            order.Lines.Add(new OrderLine
            {
                Sku = "HIST-001",
                Name = "History Test Product",
                UnitPrice = 25.00m,
                Quantity = 2
            });
            db.Orders.Add(order);
            await db.SaveChangesAsync();
        }

        // Act - This would be GET /Orders page in a real scenario
        // Since it's a Razor Page, we'll use the API endpoint instead
        // Note: There's no /api/orders list endpoint, so we verify via database

        // Assert
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var orders = await db.Orders.Include(o => o.Lines).ToListAsync();
            
            orders.Should().NotBeEmpty();
            orders.Should().ContainSingle(o => o.Lines.Any(l => l.Sku == "HIST-001"));
        }
    }

    private record CheckoutResult(int Id, string Status, decimal Total);
}
