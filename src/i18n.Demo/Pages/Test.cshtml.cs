using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Threading.Tasks;

namespace i18n.Demo.Views.Home
{
    public class Index1Model : PageModel
    {
        public async Task<IActionResult> OnGet()
        {
            return RedirectToPage("/NonExistant");
        }
    }
}
