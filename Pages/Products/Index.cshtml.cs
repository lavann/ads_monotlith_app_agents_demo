using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RetailMonolith.Data;
using RetailMonolith.Models;
using RetailMonolith.Services;
using System.Threading.Tasks;

namespace RetailMonolith.Pages.Products
{
    public class IndexModel : PageModel
    {
        private readonly AppDbContext _db;
        private readonly ICartService _cartService;
        public IndexModel(AppDbContext db, ICartService cartService)
        {
            _db = db;
            _cartService = cartService;
        }

        public IList<Product> Products { get; set; } = new List<Product>();

        // Category ? Image mapping
        public static readonly Dictionary<string, string> CategoryImages = new()
        {
            ["Beauty"] = "https://images.unsplash.com/photo-1596462502278-27bfdc403348?auto=format&fit=crop&w=800&q=80",
            ["Apparel"] = "https://images.unsplash.com/photo-1489987707025-afc232f7ea0f?auto=format&fit=crop&w=800&q=80",
            ["Footwear"] = "https://images.unsplash.com/photo-1603808033192-082d6919d3e1?auto=format&fit=crop&w=800&q=80",
            ["Home"] = "https://images.unsplash.com/photo-1583847268964-b28dc8f51f92?auto=format&fit=crop&w=800&q=80",
            ["Accessories"] = "https://images.unsplash.com/photo-1586878341523-7acb55eb8c12?auto=format&fit=crop&w=800&q=80",
            ["Electronics"] = "https://images.unsplash.com/photo-1498049794561-7780e7231661?auto=format&fit=crop&w=800&q=80"
        };

        // Helper method accessible from the Razor page
        public string GetImageForCategory(string category)
        {
            if (CategoryImages.TryGetValue(category ?? string.Empty, out var url))
                return url;

            // Fallback image if category missing
            return "https://images.unsplash.com/photo-1526170375885-4d8ecf77b99f?auto=format&fit=crop&w=800&q=80";
        }

        public async Task OnGetAsync() => Products = await _db.Products.Where(p => p.IsActive).ToListAsync();

        public async Task OnPostAsync(int productId)
        {
            // Add to cart logic will go here in the future
            var p = await _db.Products.FindAsync(productId);
            if (p is null) return;

            var cart = await _db.Carts
                .Include(c => c.Lines)
                .FirstOrDefaultAsync(c => c.CustomerId == "guest")
                ?? new Models.Cart { CustomerId = "guest" };

            //if(cart.Id == 0)
            //{
            //    _db.Carts.Add(cart);
            //};

            if (cart is null)
            {
                cart = new Models.Cart { CustomerId = "guest" };
                _db.Carts.Add(cart);
                await _db.SaveChangesAsync();
            }

            cart.Lines.Add(new CartLine
            {
                Sku = p.Sku,
                Name = p.Name,
                UnitPrice = p.Price,
                Quantity = 1
            });
            await _cartService.AddToCartAsync("guest", productId);
            Response.Redirect("/Cart");
        }
    }
}
