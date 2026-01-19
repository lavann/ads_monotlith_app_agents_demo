using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RetailMonolith.Services;
using System.Threading.Tasks;

namespace RetailMonolith.Pages.Checkout
{
    public class IndexModel : PageModel
    {
        private readonly ICartService _cartService;
        private readonly ICheckoutService _checkoutService;
        
        public IndexModel(ICartService cartService, ICheckoutService checkoutService)
        {
            _cartService = cartService;
            _checkoutService = checkoutService;
        }

        // For simplicity, using a hardcoded customer ID
        // In a real application, this would come from the authenticated user context
        // or session  
        public List<(string Name, int Qty, decimal Price)> Lines { get; set; } = new();

        public decimal Total => Lines.Sum(l => l.Price * l.Qty);

        [BindProperty]
        public string PaymentToken { get; set; } = "tok_test";

        public async Task OnGetAsync()
        {
            var cart = await _cartService.GetCartWithLinesAsync("guest");
            Lines = cart.Lines
                .Select(line => (line.Name, line.Quantity, line.UnitPrice))
                .ToList();
        }

        public async Task<IActionResult> OnPostAsync()
        {
           if(!ModelState.IsValid)
           {
                await OnGetAsync();
                return Page();
            }

            //perform checkout using MockPaymentGateway
            var order = await _checkoutService.CheckoutAsync("guest", PaymentToken);

            // redirect to order confirmation page
            return Redirect($"/Orders/Details?id={order.Id}");
        }
    }
}
