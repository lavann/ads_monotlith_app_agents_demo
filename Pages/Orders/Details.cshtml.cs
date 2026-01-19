using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RetailMonolith.Data;
using RetailMonolith.Models;

namespace RetailMonolith.Pages.Orders
{
    public class DetailsModel : PageModel
    {
        private readonly AppDbContext _db;
        public DetailsModel(AppDbContext db) => _db = db;

        public Order? Order { get; set; }
        public async Task OnGetAsync(int id)
        {
            Order = await _db.Orders
                .Include(o => o.Lines)
                .FirstOrDefaultAsync(o => o.Id == id);
        }
    }
}
