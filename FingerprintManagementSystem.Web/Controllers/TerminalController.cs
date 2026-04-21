using Microsoft.AspNetCore.Mvc;

using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Http;

namespace FingerprintManagementSystem.Web.Controllers;

public class TerminalsController : Controller
{
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        // Admin-only page (uses the same Session flag set in HomeController).
        var isAdmin = HttpContext.Session.GetString("IsAdmin") == "1";
        if (!isAdmin)
        {
            // For MVC pages, show the existing Forbidden view instead of the default Forbid() response.
            HttpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Result = View("Forbidden");
            return;
        }

        base.OnActionExecuting(context);
    }

    public IActionResult Index()
    {
        return View();
    }
}
