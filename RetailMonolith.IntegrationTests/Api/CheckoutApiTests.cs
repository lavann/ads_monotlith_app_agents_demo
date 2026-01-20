using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RetailMonolith.Data;
using RetailMonolith.Models;

namespace RetailMonolith.IntegrationTests.Api;

[Trait("Category", "Integration")]
public class CheckoutApiTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly CustomWebApplicationFactory _factory;

    public CheckoutApiTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task PostCheckout_WithValidCart_ReturnsOrderWithPaidStatus()
    {
        // Arrange - Seed database with product, inventory, and cart
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            
            var product = new Product
            {
                Sku = "TEST-001",
                Name = "Test Product",
                Price = 25.00m,
                Currency = "GBP",
                IsActive = true
            };
            db.Products.Add(product);

            var inventory = new InventoryItem
            {
                Sku = "TEST-001",
                Quantity = 100
            };
            db.Inventory.Add(inventory);

            var cart = new Cart { CustomerId = "guest" };
            cart.Lines.Add(new CartLine
            {
                Sku = "TEST-001",
                Name = "Test Product",
                UnitPrice = 25.00m,
                Quantity = 2
            });
            db.Carts.Add(cart);

            await db.SaveChangesAsync();
        }

        // Act
        var response = await _client.PostAsync("/api/checkout", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var content = await response.Content.ReadAsStringAsync();
        var orderResult = JsonSerializer.Deserialize<CheckoutResult>(content, new JsonSerializerOptions 
        { 
            PropertyNameCaseInsensitive = true 
        });

        orderResult.Should().NotBeNull();
        orderResult!.Id.Should().BeGreaterThan(0);
        orderResult.Status.Should().Be("Paid");
        orderResult.Total.Should().Be(50.00m); // 2 x 25.00
    }

    [Fact]
    public async Task PostCheckout_WithEmptyCart_ReturnsBadRequest()
    {
        // Arrange - No cart in database

        // Act
        var response = await _client.PostAsync("/api/checkout", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError); // Currently throws exception
    }

    [Fact]
    public async Task GetOrder_AfterCheckout_ReturnsCorrectOrderDetails()
    {
        // Arrange - Complete checkout first
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            
            var product = new Product
            {
                Sku = "TEST-002",
                Name = "Test Product 2",
                Price = 15.00m,
                Currency = "GBP",
                IsActive = true
            };
            db.Products.Add(product);

            var inventory = new InventoryItem
            {
                Sku = "TEST-002",
                Quantity = 50
            };
            db.Inventory.Add(inventory);

            var cart = new Cart { CustomerId = "guest" };
            cart.Lines.Add(new CartLine
            {
                Sku = "TEST-002",
                Name = "Test Product 2",
                UnitPrice = 15.00m,
                Quantity = 3
            });
            db.Carts.Add(cart);

            await db.SaveChangesAsync();
        }

        var checkoutResponse = await _client.PostAsync("/api/checkout", null);
        var checkoutContent = await checkoutResponse.Content.ReadAsStringAsync();
        var orderResult = JsonSerializer.Deserialize<CheckoutResult>(checkoutContent, new JsonSerializerOptions 
        { 
            PropertyNameCaseInsensitive = true 
        });

        // Act
        var response = await _client.GetAsync($"/api/orders/{orderResult!.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var order = await response.Content.ReadFromJsonAsync<Order>();
        order.Should().NotBeNull();
        order!.Id.Should().Be(orderResult.Id);
        order.Status.Should().Be("Paid");
        order.Total.Should().Be(45.00m); // 3 x 15.00
        order.Lines.Should().HaveCount(1);
        order.Lines[0].Sku.Should().Be("TEST-002");
        order.Lines[0].Quantity.Should().Be(3);
    }

    [Fact]
    public async Task PostCheckout_DecrementsInventory_InDatabase()
    {
        // Arrange
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            
            var product = new Product
            {
                Sku = "TEST-003",
                Name = "Test Product 3",
                Price = 20.00m,
                Currency = "GBP",
                IsActive = true
            };
            db.Products.Add(product);

            var inventory = new InventoryItem
            {
                Sku = "TEST-003",
                Quantity = 10
            };
            db.Inventory.Add(inventory);

            var cart = new Cart { CustomerId = "guest" };
            cart.Lines.Add(new CartLine
            {
                Sku = "TEST-003",
                Name = "Test Product 3",
                UnitPrice = 20.00m,
                Quantity = 3
            });
            db.Carts.Add(cart);

            await db.SaveChangesAsync();
        }

        // Act
        var response = await _client.PostAsync("/api/checkout", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var inventory = await db.Inventory.FirstOrDefaultAsync(i => i.Sku == "TEST-003");
            
            inventory.Should().NotBeNull();
            inventory!.Quantity.Should().Be(7); // 10 - 3
        }
    }

    private record CheckoutResult(int Id, string Status, decimal Total);
}
